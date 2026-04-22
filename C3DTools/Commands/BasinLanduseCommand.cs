using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using C3DTools.Services;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace C3DTools.Commands
{
    public class BasinLanduseCommand
    {
        private const string AppName = "C3DTools_Basin";

        [CommandMethod("BASINLANDUSE")]
        public void BasinLanduse()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Step 1: Auto-collect all basin-tagged polylines from model space.
            // RowKey = handle string — always unique, handles duplicate basin IDs.
            var polylineRows = new List<(string RowKey, string BasinId, string Boundary, string Development)>();
            var polylineGeoms = new Dictionary<string, Geometry>(); // keyed by RowKey

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;

                    Polyline? pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    string? basinId = GetPolylineId(pline);
                    if (string.IsNullOrEmpty(basinId)) continue;

                    Geometry? geom = GeometryConverter.PolylineToNts(pline);
                    if (geom != null && geom.IsValid)
                    {
                        string rowKey = oid.Handle.ToString();
                        string boundary = GetPolylineBoundary(pline) ?? string.Empty;
                        string development = GetPolylineDevelopment(pline) ?? string.Empty;
                        polylineRows.Add((rowKey, basinId, boundary, development));
                        polylineGeoms[rowKey] = geom;
                    }
                    else
                    {
                        ed.WriteMessage($"\nWarning: Polyline '{basinId}' has invalid geometry. Skipping.");
                    }
                }

                tr.Commit();
            }

            if (polylineRows.Count == 0)
            {
                ed.WriteMessage("\nNo basin-tagged polylines found in the drawing.");
                return;
            }

            ed.WriteMessage($"\nFound {polylineRows.Count} basin polyline(s).");

            // Step 2: Auto-collect hatches by configured layer patterns
            var resolver = new SettingsResolver();
            var settings = resolver.Resolve(db);

            if (settings.LanduseHatchLayers.Count == 0)
            {
                ed.WriteMessage("\nNo landuse hatch layers configured. Open the Basin Tools palette and add layer patterns under Settings.");
                return;
            }

            var matchedLayers = LayerMatcher.GetMatchingLayers(db, settings.LanduseHatchLayers, out var unmatchedPatterns);

            foreach (string pattern in unmatchedPatterns)
                ed.WriteMessage($"\nWarning: Layer pattern '{pattern}' matched no layers in this drawing.");

            if (matchedLayers.Count == 0)
            {
                ed.WriteMessage("\nNo layers matched the configured patterns. Command cancelled.");
                return;
            }

            var matchedLayerSet = new HashSet<string>(matchedLayers, System.StringComparer.OrdinalIgnoreCase);
            var hatchIds = new List<ObjectId>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Hatch))))
                        continue;
                    Hatch? hatch = tr.GetObject(oid, OpenMode.ForRead) as Hatch;
                    if (hatch != null && matchedLayerSet.Contains(hatch.Layer ?? "0"))
                        hatchIds.Add(oid);
                }
                tr.Commit();
            }

            if (hatchIds.Count == 0)
            {
                ed.WriteMessage($"\nNo hatches found on matched layers ({string.Join(", ", matchedLayers)}). Command cancelled.");
                return;
            }

            ed.WriteMessage($"\nAuto-collected {hatchIds.Count} hatch(es) from {matchedLayers.Count} layer(s).");

            // Step 3: Convert hatches to NTS geometries
            var hatchGeoms = new List<(Geometry Geom, string Layer)>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId oid in hatchIds)
                {
                    Hatch? hatch = tr.GetObject(oid, OpenMode.ForRead) as Hatch;
                    if (hatch == null) continue;
                    string layer = hatch.Layer ?? "0";

                    Geometry? geom = GeometryConverter.HatchToNts(hatch);
                    if (geom != null && geom.IsValid)
                    {
                        hatchGeoms.Add((geom, layer));
                    }
                    else
                    {
                        ed.WriteMessage($"\nWarning: Hatch {oid.Handle} (layer: {layer}) has invalid geometry.");

                        PromptKeywordOptions pko = new PromptKeywordOptions(
                            $"\nFix hatch {oid.Handle} (layer: {layer})? [Yes/No] <Yes>: ");
                        pko.Keywords.Add("Yes");
                        pko.Keywords.Add("No");
                        pko.Keywords.Default = "Yes";
                        pko.AllowNone = true;

                        PromptResult pkr = ed.GetKeywords(pko);
                        bool fix = pkr.Status == PromptStatus.None ||
                                   (pkr.Status == PromptStatus.OK &&
                                    string.Equals(pkr.StringResult, "Yes", System.StringComparison.OrdinalIgnoreCase));

                        if (fix)
                        {
                            BlockTableRecord btr =
                                (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                            Geometry? fixedGeom = HatchFixer.TryFixHatch(hatch, tr, btr, ed);
                            if (fixedGeom != null)
                                hatchGeoms.Add((fixedGeom, layer));
                            else
                                ed.WriteMessage($"\n  Hatch {oid.Handle} could not be fixed. Skipping.");
                        }
                        else
                        {
                            ed.WriteMessage($"\n  Skipping hatch {oid.Handle}.");
                        }
                    }
                }

                tr.Commit();
            }

            // Step 4: Compute overlap areas per row per layer
            var results = new Dictionary<string, Dictionary<string, double>>(); // keyed by RowKey
            foreach (var (rowKey, _, _, _) in polylineRows)
                results[rowKey] = new Dictionary<string, double>();

            var allLayers = new List<string>();

            foreach (var (hatchGeom, layer) in hatchGeoms)
            {
                if (!allLayers.Contains(layer))
                    allLayers.Add(layer);

                foreach (var (rowKey, _, _, _) in polylineRows)
                {
                    Geometry intersection = polylineGeoms[rowKey].Intersection(hatchGeom);
                    if (intersection != null && !intersection.IsEmpty && intersection.Area > 0)
                    {
                        if (!results[rowKey].ContainsKey(layer))
                            results[rowKey][layer] = 0;
                        results[rowKey][layer] += intersection.Area;
                    }
                }
            }

            allLayers.Sort();

            const string areaFmt = "F4";
            const string boundaryHeader = "Boundary";
            const string developmentHeader = "Development";

            // Area unit conversion
            bool toAcres = settings.AreaUnit == Models.AreaUnit.Acres;
            const double SqFtPerAcre = 43560.0;
            string areaUnitLabel = toAcres ? "ac" : "sf";
            double ConvertArea(double sf) => toAcres ? sf / SqFtPerAcre : sf;

            // Check if we have any basins with Boundary or Development attributes
            bool hasBoundary = polylineRows.Any(r => !string.IsNullOrEmpty(r.Boundary));
            bool hasDevelopment = polylineRows.Any(r => !string.IsNullOrEmpty(r.Development));

            // Step 5: Aggregate by (BasinId, Boundary, Development) — sums multiple pieces with the
            // same basin ID and attributes into one row.
            var aggregated = new Dictionary<(string BasinId, string Boundary, string Development), Dictionary<string, double>>();

            foreach (var (rowKey, basinId, boundary, development) in polylineRows)
            {
                var key = (basinId, boundary, development);
                if (!aggregated.ContainsKey(key))
                    aggregated[key] = new Dictionary<string, double>();

                foreach (var kvp in results[rowKey])
                {
                    if (!aggregated[key].ContainsKey(kvp.Key))
                        aggregated[key][kvp.Key] = 0;
                    aggregated[key][kvp.Key] += kvp.Value;
                }
            }

            // Sort basins using natural alphanumeric ordering (letters first, then numbers)
            var basinOrder = polylineRows.Select(r => r.BasinId).Distinct().OrderBy(id => id, new NaturalBasinIdComparer()).ToList();

            // Build ordered display rows. Group by basin ID, then by Development, then by Boundary
            var displayRows = new List<(string BasinId, string Boundary, string Development, Dictionary<string, double> Areas)>();

            foreach (string basinId in basinOrder)
            {
                // Get all combinations for this basin
                var basinKeys = aggregated.Keys.Where(k => k.BasinId == basinId).ToList();

                // Order: first by development (empty, Pre, Post), then by boundary (empty, Onsite, Offsite)
                var ordered = basinKeys.OrderBy(k => string.IsNullOrEmpty(k.Development) ? 0 : k.Development == "Pre" ? 1 : 2)
                                      .ThenBy(k => string.IsNullOrEmpty(k.Boundary) ? 0 : k.Boundary == "Onsite" ? 1 : 2);

                foreach (var key in ordered)
                {
                    displayRows.Add((key.BasinId, key.Boundary, key.Development, aggregated[key]));
                }
            }

            // Step 6: Build output strings
            // Pre-calculate row totals indexed to match displayRows
            var rowTotals = new List<double>();
            foreach (var (_, _, _, areas) in displayRows)
            {
                double total = 0;
                foreach (string layer in allLayers)
                    if (areas.TryGetValue(layer, out double a))
                        total += a;
                rowTotals.Add(total);
            }

            var allColumns = new List<string>(allLayers) { "Total" };

            // Append unit label to each layer column header and Total
            var columnLabels = new Dictionary<string, string>();
            foreach (string col in allColumns)
                columnLabels[col] = $"{col} ({areaUnitLabel})";

            // Column widths for aligned monospace display
            int idWidth = basinOrder.Concat(new[] { "id" }).Max(s => s.Length);
            int boundaryWidth = hasBoundary
                ? displayRows.Select(r => r.Boundary).Concat(new[] { boundaryHeader }).Max(s => s.Length)
                : 0;
            int developmentWidth = hasDevelopment
                ? displayRows.Select(r => r.Development).Concat(new[] { developmentHeader }).Max(s => s.Length)
                : 0;

            var colWidths = new Dictionary<string, int>();
            foreach (string col in allColumns)
                colWidths[col] = columnLabels[col].Length;
            for (int i = 0; i < displayRows.Count; i++)
            {
                foreach (string layer in allLayers)
                    if (displayRows[i].Areas.TryGetValue(layer, out double a))
                        colWidths[layer] = System.Math.Max(colWidths[layer], ConvertArea(a).ToString(areaFmt).Length);
                colWidths["Total"] = System.Math.Max(colWidths["Total"], ConvertArea(rowTotals[i]).ToString(areaFmt).Length);
            }

            // Space-padded display text
            var displaySb = new StringBuilder();
            displaySb.Append("id".PadRight(idWidth));
            if (hasBoundary)
                displaySb.Append("  " + boundaryHeader.PadRight(boundaryWidth));
            if (hasDevelopment)
                displaySb.Append("  " + developmentHeader.PadRight(developmentWidth));
            foreach (string col in allColumns)
                displaySb.Append("  " + columnLabels[col].PadLeft(colWidths[col]));
            displaySb.AppendLine();
            displaySb.Append(new string('-', idWidth));
            if (hasBoundary)
                displaySb.Append("  " + new string('-', boundaryWidth));
            if (hasDevelopment)
                displaySb.Append("  " + new string('-', developmentWidth));
            foreach (string col in allColumns)
                displaySb.Append("  " + new string('-', colWidths[col]));
            displaySb.AppendLine();

            for (int i = 0; i < displayRows.Count; i++)
            {
                var (basinId, boundary, development, areas) = displayRows[i];

                displaySb.Append(basinId.PadRight(idWidth));
                if (hasBoundary)
                    displaySb.Append("  " + (boundary ?? "").PadRight(boundaryWidth));
                if (hasDevelopment)
                    displaySb.Append("  " + (development ?? "").PadRight(developmentWidth));
                foreach (string layer in allLayers)
                {
                    string cell = areas.TryGetValue(layer, out double a) ? ConvertArea(a).ToString(areaFmt) : "";
                    displaySb.Append("  " + cell.PadLeft(colWidths[layer]));
                }
                displaySb.Append("  " + ConvertArea(rowTotals[i]).ToString(areaFmt).PadLeft(colWidths["Total"]));
                displaySb.AppendLine();

                // Blank line after each basin's last row
                bool isLastRowForBasin = i == displayRows.Count - 1
                    || displayRows[i + 1].BasinId != basinId;
                if (isLastRowForBasin && i < displayRows.Count - 1)
                    displaySb.AppendLine();
            }

            // Tab-separated clipboard text
            var clipSb = new StringBuilder();
            clipSb.Append("id");
            if (hasBoundary) { clipSb.Append("\t"); clipSb.Append(boundaryHeader); }
            if (hasDevelopment) { clipSb.Append("\t"); clipSb.Append(developmentHeader); }
            foreach (string col in allColumns) { clipSb.Append("\t"); clipSb.Append(columnLabels[col]); }
            clipSb.AppendLine();

            for (int i = 0; i < displayRows.Count; i++)
            {
                var (basinId, boundary, development, areas) = displayRows[i];

                // Use Excel formula syntax to force Basin ID as text (prevents date conversion)
                clipSb.Append("=\"");
                clipSb.Append(basinId);
                clipSb.Append("\"");
                if (hasBoundary) { clipSb.Append("\t"); clipSb.Append(boundary ?? ""); }
                if (hasDevelopment) { clipSb.Append("\t"); clipSb.Append(development ?? ""); }
                foreach (string layer in allLayers)
                {
                    clipSb.Append("\t");
                    if (areas.TryGetValue(layer, out double ea))
                        clipSb.Append(ConvertArea(ea).ToString(areaFmt));
                }
                clipSb.Append("\t");
                clipSb.Append(ConvertArea(rowTotals[i]).ToString(areaFmt));
                clipSb.AppendLine();
            }

            // Step 7: Show results dialog
            var form = new C3DTools.UI.BasinLanduseResultForm(displaySb.ToString(), clipSb.ToString());
            Application.ShowModalDialog(form);
        }

        /// <summary>
        /// Retrieves the Boundary attribute from a polyline's XData.
        /// Returns the boundary string ("Onsite", "Offsite") or empty string if not set.
        /// </summary>
        private string? GetPolylineBoundary(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppName);
            if (rb == null) return string.Empty;

            TypedValue[] values = rb.AsArray();
            rb.Dispose();

            // XData structure: [AppName, BasinId, Boundary, Development]
            if (values.Length > 2 && values[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                return values[2].Value?.ToString() ?? string.Empty;

            return string.Empty;
        }

        /// <summary>
        /// Retrieves the Development attribute from a polyline's XData.
        /// Returns the development string ("Pre", "Post") or empty string if not set.
        /// </summary>
        private string? GetPolylineDevelopment(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppName);
            if (rb == null) return string.Empty;

            TypedValue[] values = rb.AsArray();
            rb.Dispose();

            // XData structure: [AppName, BasinId, Boundary, Development]
            if (values.Length > 3 && values[3].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                return values[3].Value?.ToString() ?? string.Empty;

            return string.Empty;
        }

        /// <summary>
        /// Retrieves the ID tag from a polyline's XData.
        /// Returns the ID string or null if not found.
        /// </summary>
        private string? GetPolylineId(Polyline pline)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppName);

            if (rb == null)
                return null;

            TypedValue[] values = rb.AsArray();
            if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
            {
                string? id = values[1].Value.ToString();
                rb.Dispose();
                return id;
            }

            rb.Dispose();
            return null;
        }

        /// <summary>
        /// Natural alphanumeric comparer for basin IDs.
        /// Sorts letters first (A, A1, A2, B1-1...), then numbers (1, 1-1, 1-2, 2-1...).
        /// Within each category, uses natural number ordering (A1, A2, A10 instead of A1, A10, A2).
        /// </summary>
        private class NaturalBasinIdComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                // Check if strings start with letter or digit
                bool xStartsWithLetter = x.Length > 0 && char.IsLetter(x[0]);
                bool yStartsWithLetter = y.Length > 0 && char.IsLetter(y[0]);

                // Letters come before numbers
                if (xStartsWithLetter && !yStartsWithLetter) return -1;
                if (!xStartsWithLetter && yStartsWithLetter) return 1;

                // Both start with same type, use natural sort
                return CompareNatural(x, y);
            }

            private int CompareNatural(string x, string y)
            {
                int ix = 0, iy = 0;

                while (ix < x.Length && iy < y.Length)
                {
                    char cx = x[ix];
                    char cy = y[iy];

                    // If both are digits, compare numerically
                    if (char.IsDigit(cx) && char.IsDigit(cy))
                    {
                        // Extract full numbers
                        int numX = 0;
                        while (ix < x.Length && char.IsDigit(x[ix]))
                        {
                            numX = numX * 10 + (x[ix] - '0');
                            ix++;
                        }

                        int numY = 0;
                        while (iy < y.Length && char.IsDigit(y[iy]))
                        {
                            numY = numY * 10 + (y[iy] - '0');
                            iy++;
                        }

                        if (numX != numY)
                            return numX.CompareTo(numY);
                    }
                    else
                    {
                        // Compare characters directly
                        int cmp = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                        if (cmp != 0)
                            return cmp;

                        ix++;
                        iy++;
                    }
                }

                // One string is prefix of other
                return x.Length.CompareTo(y.Length);
            }
        }
    }
}
