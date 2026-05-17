using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using C3DTools.Models;
using NetTopologySuite.Geometries;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace C3DTools.Services
{
    public class HydrologyModelExporter
    {
        private const string AppNameBasin = "C3DTools_Basin";
        private const string AppNameHydroInputs = "C3DTools_HydroInputs";
        private const double SquareFeetPerAcre = 43560.0;

        public HydrologyModel Export(Document doc)
        {
            Database db = doc.Database;
            var model = new HydrologyModel
            {
                Project = new HydrologyProject
                {
                    Name = string.IsNullOrWhiteSpace(db.Filename)
                        ? "Untitled Civil 3D Drawing"
                        : Path.GetFileNameWithoutExtension(db.Filename),
                    DrawingPath = db.Filename ?? string.Empty,
                    ExportedAtUtc = DateTime.UtcNow
                }
            };

            BasinSettings settings = new SettingsResolver().Resolve(db);

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var basinGeometries = new Dictionary<string, Geometry>();

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;

                    Polyline? pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                    if (pline == null || !pline.Closed)
                        continue;

                    BasinTag? tag = ReadBasinTag(pline);
                    if (tag == null || string.IsNullOrWhiteSpace(tag.Id))
                        continue;

                    Geometry? geometry = GeometryConverter.PolylineToNts(pline);
                    if (geometry == null || geometry.IsEmpty)
                        continue;

                    string handle = oid.Handle.ToString();
                    HydrologyInputs inputs = ReadHydrologyInputs(pline);
                    basinGeometries[handle] = geometry;
                    model.Basins.Add(new HydrologyBasin
                    {
                        Id = tag.Id,
                        Name = BuildBasinName(tag),
                        Condition = tag.Development,
                        Boundary = tag.Boundary,
                        Development = tag.Development,
                        Layer = pline.Layer ?? string.Empty,
                        Handle = handle,
                        AreaAcres = RoundArea(geometry.Area / SquareFeetPerAcre),
                        CurveNumber = inputs.CurveNumber,
                        TcMinutes = inputs.TcMinutes,
                        DownstreamId = inputs.DownstreamId,
                        Geometry = ToHydrologyGeometry(geometry)
                    });

                    if (!string.IsNullOrWhiteSpace(inputs.DownstreamId) && !IsTerminalRoute(inputs.DownstreamId))
                    {
                        model.Links.Add(new HydrologyLink
                        {
                            Id = $"{tag.Id}->{inputs.DownstreamId}",
                            Type = "basin_route",
                            FromBasin = tag.Id,
                            ToBasin = inputs.DownstreamId
                        });
                    }
                }

                var hatchGeometries = CollectLandUseHatches(db, tr, ms, settings);
                foreach (HydrologyBasin basin in model.Basins)
                {
                    if (!basinGeometries.TryGetValue(basin.Handle, out Geometry? basinGeometry))
                        continue;

                    foreach ((Geometry hatchGeometry, string layer) in hatchGeometries)
                    {
                        Geometry intersection = basinGeometry.Intersection(hatchGeometry);
                        if (intersection.IsEmpty || intersection.Area <= 0)
                            continue;

                        basin.LandUses.Add(new HydrologyLandUseArea
                        {
                            Id = layer,
                            Layer = layer,
                            AreaAcres = RoundArea(intersection.Area / SquareFeetPerAcre),
                            CurveNumber = TryReadCurveNumber(layer)
                        });
                    }
                }

                tr.Commit();
            }

            model.Basins = model.Basins
                .OrderBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Development, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Boundary, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            model.Hydrographs = new HydrologyHydrographService().GetHydrographs(db);

            return model;
        }

        private static List<(Geometry Geometry, string Layer)> CollectLandUseHatches(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            BasinSettings settings)
        {
            var hatches = new List<(Geometry Geometry, string Layer)>();
            if (settings.LanduseHatchLayers.Count == 0)
                return hatches;

            var matchedLayers = LayerMatcher.GetMatchingLayers(db, settings.LanduseHatchLayers, out _);
            var matchedLayerSet = new HashSet<string>(matchedLayers, StringComparer.OrdinalIgnoreCase);
            if (matchedLayerSet.Count == 0)
                return hatches;

            foreach (ObjectId oid in ms)
            {
                if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Hatch))))
                    continue;

                Hatch? hatch = tr.GetObject(oid, OpenMode.ForRead) as Hatch;
                if (hatch == null || !matchedLayerSet.Contains(hatch.Layer ?? "0"))
                    continue;

                Geometry? geometry = GeometryConverter.HatchToNts(hatch);
                if (geometry != null && geometry.IsValid && !geometry.IsEmpty)
                    hatches.Add((geometry, hatch.Layer ?? "0"));
            }

            return hatches;
        }

        private static BasinTag? ReadBasinTag(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
            if (rb == null)
                return null;

            try
            {
                TypedValue[] values = rb.AsArray();
                return new BasinTag(
                    ReadXDataString(values, 1),
                    ReadXDataString(values, 2),
                    ReadXDataString(values, 3));
            }
            finally
            {
                rb.Dispose();
            }
        }

        private static string ReadXDataString(TypedValue[] values, int index)
        {
            if (values.Length <= index || values[index].TypeCode != (int)DxfCode.ExtendedDataAsciiString)
                return string.Empty;

            return values[index].Value?.ToString() ?? string.Empty;
        }

        private static HydrologyInputs ReadHydrologyInputs(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppNameHydroInputs);
            if (rb == null)
                return new HydrologyInputs(null, string.Empty, null);

            try
            {
                TypedValue[] values = rb.AsArray();
                double? tcMinutes = null;
                string tcValue = ReadXDataString(values, 2);
                if (double.TryParse(tcValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedTc))
                    tcMinutes = parsedTc;

                double? curveNumber = null;
                string curveNumberValue = ReadXDataString(values, 4);
                if (double.TryParse(curveNumberValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedCurveNumber))
                    curveNumber = parsedCurveNumber;

                return new HydrologyInputs(tcMinutes, ReadXDataString(values, 3), curveNumber);
            }
            finally
            {
                rb.Dispose();
            }
        }

        private static bool IsTerminalRoute(string downstreamId)
        {
            return downstreamId.Equals("OUTLET", StringComparison.OrdinalIgnoreCase) ||
                   downstreamId.Equals("OUTFALL", StringComparison.OrdinalIgnoreCase) ||
                   downstreamId.Equals("NONE", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildBasinName(BasinTag tag)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(tag.Development))
                parts.Add(tag.Development);
            parts.Add(tag.Id);
            if (!string.IsNullOrWhiteSpace(tag.Boundary))
                parts.Add(tag.Boundary);
            return string.Join(" ", parts);
        }

        private static HydrologyGeometry? ToHydrologyGeometry(Geometry geometry)
        {
            Polygon? polygon = geometry switch
            {
                Polygon p => p,
                MultiPolygon mp when mp.NumGeometries > 0 => mp.GetGeometryN(0) as Polygon,
                _ => null
            };

            if (polygon == null)
                return null;

            var result = new HydrologyGeometry { Type = "Polygon" };
            result.Coordinates.Add(ToRing(polygon.ExteriorRing.Coordinates));
            for (int i = 0; i < polygon.NumInteriorRings; i++)
                result.Coordinates.Add(ToRing(polygon.GetInteriorRingN(i).Coordinates));

            return result;
        }

        private static List<List<double>> ToRing(Coordinate[] coordinates)
        {
            return coordinates
                .Select(c => new List<double> { RoundCoordinate(c.X), RoundCoordinate(c.Y) })
                .ToList();
        }

        private static double? TryReadCurveNumber(string layer)
        {
            Match match = Regex.Match(layer, @"(?:^|[^0-9])CN[\s_-]*(\d{2})(?:[^0-9]|$)", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double cn))
                return cn;

            if (layer.Contains("IMP", StringComparison.OrdinalIgnoreCase))
                return 98;

            return null;
        }

        private static double RoundArea(double value) => Math.Round(value, 6);

        private static double RoundCoordinate(double value) => Math.Round(value, 4);

        private sealed record BasinTag(string Id, string Boundary, string Development);

        private sealed record HydrologyInputs(double? TcMinutes, string DownstreamId, double? CurveNumber);
    }
}