using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
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

            // Step 2: Select hatches
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect hatches: ";

            SelectionFilter filter = new SelectionFilter(new TypedValue[] {
                new TypedValue((int)DxfCode.Start, "HATCH")
            });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo hatches selected.");
                return;
            }

            ObjectId[] hatchIds = psr.Value.GetObjectIds();

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

            // Preserve first-seen basin order
            var basinOrder = polylineRows.Select(r => r.BasinId).Distinct().ToList();

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
                colWidths[col] = col.Length;
            for (int i = 0; i < displayRows.Count; i++)
            {
                foreach (string layer in allLayers)
                    if (displayRows[i].Areas.TryGetValue(layer, out double a))
                        colWidths[layer] = System.Math.Max(colWidths[layer], a.ToString(areaFmt).Length);
                colWidths["Total"] = System.Math.Max(colWidths["Total"], rowTotals[i].ToString(areaFmt).Length);
            }

            // Space-padded display text
            var displaySb = new StringBuilder();
            displaySb.Append("id".PadRight(idWidth));
            if (hasBoundary)
                displaySb.Append("  " + boundaryHeader.PadRight(boundaryWidth));
            if (hasDevelopment)
                displaySb.Append("  " + developmentHeader.PadRight(developmentWidth));
            foreach (string col in allColumns)
                displaySb.Append("  " + col.PadLeft(colWidths[col]));
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
                    string cell = areas.TryGetValue(layer, out double a) ? a.ToString(areaFmt) : "";
                    displaySb.Append("  " + cell.PadLeft(colWidths[layer]));
                }
                displaySb.Append("  " + rowTotals[i].ToString(areaFmt).PadLeft(colWidths["Total"]));
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
            foreach (string col in allColumns) { clipSb.Append("\t"); clipSb.Append(col); }
            clipSb.AppendLine();

            for (int i = 0; i < displayRows.Count; i++)
            {
                var (basinId, boundary, development, areas) = displayRows[i];

                clipSb.Append(basinId);
                if (hasBoundary) { clipSb.Append("\t"); clipSb.Append(boundary ?? ""); }
                if (hasDevelopment) { clipSb.Append("\t"); clipSb.Append(development ?? ""); }
                foreach (string layer in allLayers)
                {
                    clipSb.Append("\t");
                    if (areas.TryGetValue(layer, out double ea))
                        clipSb.Append(ea.ToString(areaFmt));
                }
                clipSb.Append("\t");
                clipSb.Append(rowTotals[i].ToString(areaFmt));
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
    }
}
