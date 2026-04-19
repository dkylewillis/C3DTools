using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using C3DTools.Helpers;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Valid;
using System.Collections.Generic;

namespace C3DTools.Helpers
{
    /// <summary>
    /// Shared helper for validating and fixing AutoCAD hatch geometry via NTS.
    /// </summary>
    public static class HatchFixer
    {
        /// <summary>
        /// Attempts to fix a single invalid hatch in-place within an existing transaction.
        /// The original hatch is erased and replacement hatch(es) are appended to
        /// <paramref name="btr"/>. Returns the fixed NTS geometry so the caller can use it
        /// for further analysis, or <c>null</c> if fixing failed or was unnecessary.
        /// </summary>
        public static Geometry TryFixHatch(
            Hatch original,
            Transaction tr,
            BlockTableRecord btr,
            Editor ed)
        {
            Handle handle = original.Handle;
            string layer  = original.Layer ?? "0";

            // Extract NTS geometry
            Geometry ntsGeom;
            try { ntsGeom = GeometryConverter.HatchToNts(original); }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Hatch {handle}: conversion error – {ex.Message}");
                return null;
            }

            if (ntsGeom == null || ntsGeom.IsEmpty)
            {
                ed.WriteMessage($"\n  Hatch {handle}: could not extract geometry.");
                return null;
            }

            // Validate
            IsValidOp validator = new IsValidOp(ntsGeom);
            if (validator.IsValid)
                return ntsGeom; // already valid – nothing to do

            ed.WriteMessage($"\n  Hatch {handle}: {validator.ValidationError.Message}");

            // Fix
            Geometry fixedGeom;
            try { fixedGeom = GeometryFixer.Fix(ntsGeom); }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Fix error – {ex.Message}");
                return null;
            }

            // GeometryFixer cannot resolve "Hole lies outside shell" –
            // fall back to re-classifying mis-wound rings.
            if ((fixedGeom == null || fixedGeom.IsEmpty) &&
                validator.ValidationError.ErrorType ==
                    NetTopologySuite.Operation.Valid.TopologyValidationErrors.HoleOutsideShell)
            {
                fixedGeom = FixHolesOutsideShell(ntsGeom);
                if (fixedGeom != null && !fixedGeom.IsEmpty)
                    ed.WriteMessage($"\n  Applied fallback: re-classified mis-wound rings.");
            }

            if (fixedGeom == null || fixedGeom.IsEmpty)
            {
                ed.WriteMessage($"\n  Unable to fix geometry.");
                return null;
            }

            List<Polygon> polygons = FlattenPolygons(fixedGeom);
            if (polygons.Count == 0)
            {
                ed.WriteMessage($"\n  Fixed geometry has no usable polygons.");
                return null;
            }

            LineWeight       lineWeight  = original.LineWeight;
            string           patternName = original.PatternName;
            HatchPatternType patternType = original.PatternType;
            double           patScale    = original.PatternScale;
            double           patAngle    = original.PatternAngle;

            int  createdCount = 0;
            bool anyFailed    = false;

            foreach (Polygon poly in polygons)
            {
                Polyline outerPline = RingToPolyline(poly.ExteriorRing, layer, lineWeight);
                if (outerPline == null) { anyFailed = true; continue; }
                btr.AppendEntity(outerPline);
                tr.AddNewlyCreatedDBObject(outerPline, true);

                List<Polyline> holePlines = new List<Polyline>();
                for (int h = 0; h < poly.NumInteriorRings; h++)
                {
                    Polyline holePline = RingToPolyline(poly.GetInteriorRingN(h), layer, lineWeight);
                    if (holePline == null) continue;
                    btr.AppendEntity(holePline);
                    tr.AddNewlyCreatedDBObject(holePline, true);
                    holePlines.Add(holePline);
                }

                Hatch newHatch = new Hatch();
                btr.AppendEntity(newHatch);
                tr.AddNewlyCreatedDBObject(newHatch, true);

                bool hatchOk = false;
                try
                {
                    newHatch.SetHatchPattern(patternType, patternName);
                    newHatch.Layer       = layer;
                    newHatch.Associative = false;

                    ObjectIdCollection outerIds = new ObjectIdCollection();
                    outerIds.Add(outerPline.ObjectId);
                    newHatch.AppendLoop(HatchLoopTypes.Outermost, outerIds);

                    foreach (Polyline hp in holePlines)
                    {
                        ObjectIdCollection holeIds = new ObjectIdCollection();
                        holeIds.Add(hp.ObjectId);
                        newHatch.AppendLoop(HatchLoopTypes.Default, holeIds);
                    }

                    newHatch.PatternScale = patScale;
                    newHatch.PatternAngle = patAngle;
                    newHatch.EvaluateHatch(true);
                    hatchOk = true;
                    createdCount++;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n  Failed to create replacement hatch – {ex.Message}");
                    newHatch.Erase();
                    anyFailed = true;
                }

                outerPline.UpgradeOpen();
                outerPline.Erase();
                foreach (Polyline hp in holePlines) { hp.UpgradeOpen(); hp.Erase(); }

                if (hatchOk)
                {
                    string areaStr;
                    try   { areaStr = $"{newHatch.Area:F4}"; }
                    catch { areaStr = "n/a"; }
                    ed.WriteMessage($"\n  Created replacement hatch (area: {areaStr})");
                }
            }

            if (createdCount > 0)
            {
                original.UpgradeOpen();
                original.Erase();
                ed.WriteMessage($"\n  Replaced with {createdCount} hatch(es).");
                return fixedGeom;
            }

            ed.WriteMessage($"\n  Could not replace hatch {handle} – original kept.");
            return null;
        }

        /// <summary>
        /// Recursively collects all non-empty Polygon instances from any Geometry.
        /// </summary>
        public static List<Polygon> FlattenPolygons(Geometry geom)
        {
            var result = new List<Polygon>();
            if (geom is Polygon p)
            {
                if (!p.IsEmpty && p.Area > 1e-10) result.Add(p);
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    result.AddRange(FlattenPolygons(gc.GetGeometryN(i)));
            }
            return result;
        }

        /// <summary>
        /// Creates a closed LWPOLYLINE from an NTS LinearRing.
        /// Returns null if the ring has fewer than 3 unique vertices.
        /// </summary>
        public static Polyline RingToPolyline(LineString ring, string layer, LineWeight lineWeight)
        {
            Coordinate[] coords = ring.Coordinates;
            int vertCount = coords.Length - 1; // NTS closes rings with a duplicate last point
            if (vertCount < 3) return null;

            Polyline pline = new Polyline();
            pline.Layer      = layer;
            pline.LineWeight = lineWeight;

            for (int i = 0; i < vertCount; i++)
                pline.AddVertexAt(i, new Point2d(coords[i].X, coords[i].Y), 0, 0, 0);

            pline.Closed = true;
            return pline;
        }

        /// <summary>
        /// Fixes "Hole lies outside shell" by re-classifying each ring based on containment.
        /// Rings not inside any shell are promoted to their own shell (handles mis-wound
        /// clockwise boundaries that AutoCAD does not enforce winding direction for).
        /// Handles Polygon and MultiPolygon inputs; returns a Polygon or MultiPolygon.
        /// </summary>
        public static Geometry FixHolesOutsideShell(Geometry geom)
        {
            GeometryFactory gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

            var allRings = new List<LinearRing>();

            void CollectRings(Polygon poly)
            {
                if (poly.ExteriorRing is LinearRing shell) allRings.Add(shell);
                for (int i = 0; i < poly.NumInteriorRings; i++)
                    if (poly.GetInteriorRingN(i) is LinearRing hole) allRings.Add(hole);
            }

            if (geom is Polygon sp) CollectRings(sp);
            else if (geom is MultiPolygon mp)
                for (int i = 0; i < mp.NumGeometries; i++)
                    if (mp.GetGeometryN(i) is Polygon part) CollectRings(part);

            if (allRings.Count == 0) return null;

            var candidates = new List<Polygon>();
            foreach (LinearRing ring in allRings)
            {
                try
                {
                    Polygon candidate = gf.CreatePolygon(ring);
                    if (!candidate.IsEmpty && candidate.Area > 1e-10)
                        candidates.Add(candidate);
                }
                catch { }
            }

            if (candidates.Count == 0) return null;

            candidates.Sort((a, b) => b.Area.CompareTo(a.Area));

            var shells     = new List<Polygon>           { candidates[0] };
            var shellHoles = new List<List<LinearRing>>  { new List<LinearRing>() };

            for (int i = 1; i < candidates.Count; i++)
            {
                Polygon candidate    = candidates[i];
                bool    assignedHole = false;

                for (int s = 0; s < shells.Count; s++)
                {
                    try
                    {
                        if (shells[s].Contains(candidate.Centroid))
                        {
                            shellHoles[s].Add(candidate.ExteriorRing as LinearRing);
                            assignedHole = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!assignedHole)
                {
                    shells.Add(candidate);
                    shellHoles.Add(new List<LinearRing>());
                }
            }

            var result = new List<Polygon>();
            for (int s = 0; s < shells.Count; s++)
            {
                try
                {
                    LinearRing shellRing = shells[s].ExteriorRing as LinearRing;
                    result.Add(gf.CreatePolygon(shellRing, shellHoles[s].ToArray()));
                }
                catch
                {
                    result.Add(shells[s]);
                }
            }

            if (result.Count == 0) return null;
            if (result.Count == 1) return result[0];
            return gf.CreateMultiPolygon(result.ToArray());
        }
    }
}
