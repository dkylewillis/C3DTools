using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using C3DTools.Models;
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

        // ── Display row ───────────────────────────────────────────────────────────
        // MaskName = "" means no mask (whole basin). Side = "" for no-mask rows.
        private record DisplayRow(
            string BasinId,
            string Development,
            string MaskName,
            string Side,
            Dictionary<string, double> Areas);

        [CommandMethod("BASINLANDUSE")]
        public void BasinLanduse()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor ed    = doc.Editor;

            // ── Step 1: Collect tagged basin polylines ────────────────────────────
            // basins: handle → (BasinId, Development, NTS geometry)
            var basins = new List<(string Handle, string BasinId, string Development, Geometry Geom)>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt       = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
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
                    if (geom == null || !geom.IsValid)
                    {
                        ed.WriteMessage($"\nWarning: Basin '{basinId}' has invalid geometry. Skipping.");
                        continue;
                    }

                    string development = GetPolylineDevelopment(pline) ?? string.Empty;
                    basins.Add((oid.Handle.ToString(), basinId, development, geom));
                }

                tr.Commit();
            }

            if (basins.Count == 0)
            {
                ed.WriteMessage("\nNo basin-tagged polylines found in the drawing.");
                return;
            }

            ed.WriteMessage($"\nFound {basins.Count} basin polyline(s).");

            // ── Step 2: Load masks from NOD ───────────────────────────────────────
            var maskService = new MaskService();
            List<MaskDefinition> masks = maskService.GetMasks(db);

            // Resolve each mask to an NTS geometry (skip any that can't be resolved)
            var maskGeoms = new List<(MaskDefinition Mask, Geometry Geom)>();

            if (masks.Count > 0)
            {
                using Transaction tr = db.TransactionManager.StartTransaction();

                foreach (MaskDefinition mask in masks)
                {
                    ObjectId maskOid = maskService.ResolvePolyline(db, mask.PolylineHandle);
                    if (maskOid.IsNull || maskOid.IsErased)
                    {
                        ed.WriteMessage($"\nWarning: Mask '{mask.Name}' polyline not found or was erased (handle {mask.PolylineHandle}). Skipping.");
                        continue;
                    }

                    Polyline? maskPline = tr.GetObject(maskOid, OpenMode.ForRead) as Polyline;
                    if (maskPline == null || !maskPline.Closed)
                    {
                        ed.WriteMessage($"\nWarning: Mask '{mask.Name}' polyline is not a closed polyline. Skipping.");
                        continue;
                    }

                    Geometry? maskGeom = GeometryConverter.PolylineToNts(maskPline);
                    if (maskGeom == null || !maskGeom.IsValid)
                    {
                        ed.WriteMessage($"\nWarning: Mask '{mask.Name}' has invalid geometry. Skipping.");
                        continue;
                    }

                    maskGeoms.Add((mask, maskGeom));
                }

                tr.Commit();
            }

            if (maskGeoms.Count > 0)
                ed.WriteMessage($"\nUsing {maskGeoms.Count} mask(s): {string.Join(", ", maskGeoms.Select(m => m.Mask.Name))}");
            else
                ed.WriteMessage("\nNo masks defined — computing whole-basin landuse.");

            // ── Step 3: Build the set of (basin × mask-side) geometry slices ──────
            // Each slice is the piece of basin geometry we will intersect with hatches.
            // sliceKey → (BasinId, Development, MaskName, Side, SliceGeom)
            var slices = new List<(string SliceKey, string BasinId, string Development, string MaskName, string Side, Geometry SliceGeom)>();

            foreach (var (handle, basinId, development, basinGeom) in basins)
            {
                if (maskGeoms.Count == 0)
                {
                    // No masks: whole basin is one slice
                    slices.Add(($"{handle}::none", basinId, development, "", "", basinGeom));
                }
                else
                {
                    foreach (var (mask, maskGeom) in maskGeoms)
                    {
                        // Inside slice
                        Geometry inside = BooleanOperationHelper.Intersect(basinGeom, maskGeom);
                        if (!inside.IsEmpty)
                            slices.Add(($"{handle}::{mask.Name}::in", basinId, development, mask.Name, mask.InsideLabel, inside));

                        // Outside slice
                        Geometry outside = BooleanOperationHelper.Difference(basinGeom, maskGeom);
                        if (!outside.IsEmpty)
                            slices.Add(($"{handle}::{mask.Name}::out", basinId, development, mask.Name, mask.OutsideLabel, outside));
                    }
                }
            }

            // ── Step 4: Collect hatches by configured layer patterns ───────────────
            var settings = new SettingsResolver().Resolve(db);

            if (settings.LanduseHatchLayers.Count == 0)
            {
                ed.WriteMessage("\nNo landuse hatch layers configured. Open the Basin Tools palette → Landuses tab.");
                return;
            }

            var matchedLayers = LayerMatcher.GetMatchingLayers(db, settings.LanduseHatchLayers, out var unmatchedPatterns);

            foreach (string pattern in unmatchedPatterns)
                ed.WriteMessage($"\nWarning: Layer pattern '{pattern}' matched no layers.");

            if (matchedLayers.Count == 0)
            {
                ed.WriteMessage("\nNo layers matched the configured patterns. Command cancelled.");
                return;
            }

            var matchedLayerSet = new HashSet<string>(matchedLayers, System.StringComparer.OrdinalIgnoreCase);
            var hatchIds = new List<ObjectId>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt       = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Hatch))))
                        continue;
                    Hatch? h = tr.GetObject(oid, OpenMode.ForRead) as Hatch;
                    if (h != null && matchedLayerSet.Contains(h.Layer ?? "0"))
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

            // ── Step 5: Convert hatches to NTS geometries ─────────────────────────
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
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
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

            // ── Step 6: Intersect each slice with each hatch ──────────────────────
            // rawAreas[sliceKey][layer] = area (sq units)
            var rawAreas  = slices.ToDictionary(s => s.SliceKey, _ => new Dictionary<string, double>());
            var allLayers = new List<string>();

            foreach (var (hatchGeom, layer) in hatchGeoms)
            {
                if (!allLayers.Contains(layer))
                    allLayers.Add(layer);

                foreach (var slice in slices)
                {
                    Geometry ix = BooleanOperationHelper.Intersect(slice.SliceGeom, hatchGeom);
                    if (ix.IsEmpty || ix.Area <= 0) continue;

                    if (!rawAreas[slice.SliceKey].ContainsKey(layer))
                        rawAreas[slice.SliceKey][layer] = 0;
                    rawAreas[slice.SliceKey][layer] += ix.Area;
                }
            }

            allLayers.Sort();

            // ── Step 7: Aggregate into display rows ───────────────────────────────
            // Group key: (BasinId, Development, MaskName, Side)
            // Multiple polylines with the same basin ID and same dev/mask/side are summed.
            var aggKey   = new Dictionary<(string, string, string, string), Dictionary<string, double>>();

            foreach (var slice in slices)
            {
                var key = (slice.BasinId, slice.Development, slice.MaskName, slice.Side);
                if (!aggKey.ContainsKey(key))
                    aggKey[key] = new Dictionary<string, double>();

                foreach (var kvp in rawAreas[slice.SliceKey])
                {
                    aggKey[key].TryGetValue(kvp.Key, out double existing);
                    aggKey[key][kvp.Key] = existing + kvp.Value;
                }
            }

            // ── Step 8: Sort and flatten to ordered display rows ──────────────────
            var basinOrder = basins
                .Select(b => b.BasinId).Distinct()
                .OrderBy(id => id, new NaturalBasinIdComparer())
                .ToList();

            bool hasMasks       = maskGeoms.Count > 0;
            bool hasDevelopment = basins.Any(b => !string.IsNullOrEmpty(b.Development));

            var displayRows = new List<DisplayRow>();

            foreach (string basinId in basinOrder)
            {
                var rows = aggKey.Keys
                    .Where(k => k.Item1 == basinId)
                    .OrderBy(k => string.IsNullOrEmpty(k.Item2) ? 0 : k.Item2 == "Pre" ? 1 : 2)  // Development
                    .ThenBy(k => k.Item3)                                                           // MaskName alpha
                    .ThenBy(k => k.Item4);                                                          // Side label alpha

                foreach (var key in rows)
                    displayRows.Add(new DisplayRow(key.Item1, key.Item2, key.Item3, key.Item4, aggKey[key]));
            }

            // ── Step 9: Build output ──────────────────────────────────────────────
            const string areaFmt     = "F4";
            const string maskHeader  = "Mask";
            const string sideHeader  = "Side";
            const string devHeader   = "Development";

            bool toAcres = settings.AreaUnit == AreaUnit.Acres;
            const double SqFtPerAcre = 43560.0;
            string areaUnitLabel = toAcres ? "ac" : "sf";
            double ConvertArea(double sf) => toAcres ? sf / SqFtPerAcre : sf;

            // Row totals
            var rowTotals = displayRows
                .Select(r => allLayers.Where(l => r.Areas.ContainsKey(l)).Sum(l => r.Areas[l]))
                .ToList();

            var allColumns    = allLayers.Concat(new[] { "Total" }).ToList();
            var columnLabels  = allColumns.ToDictionary(c => c, c => $"{c} ({areaUnitLabel})");

            // Column widths
            int idWidth  = basinOrder.Append("id").Max(s => s.Length);
            int devWidth = hasDevelopment
                ? displayRows.Select(r => r.Development).Append(devHeader).Max(s => s.Length) : 0;
            int maskWidth = hasMasks
                ? displayRows.Select(r => r.MaskName).Append(maskHeader).Max(s => s.Length) : 0;
            int sideWidth = hasMasks
                ? displayRows.Select(r => r.Side).Append(sideHeader).Max(s => s.Length) : 0;

            var colWidths = allColumns.ToDictionary(c => c, c => columnLabels[c].Length);
            for (int i = 0; i < displayRows.Count; i++)
            {
                foreach (string l in allLayers)
                {
                    if (displayRows[i].Areas.TryGetValue(l, out double a))
                        colWidths[l] = System.Math.Max(colWidths[l], ConvertArea(a).ToString(areaFmt).Length);
                }
                colWidths["Total"] = System.Math.Max(colWidths["Total"], ConvertArea(rowTotals[i]).ToString(areaFmt).Length);
            }

            // Helper: append fixed-width cell to a StringBuilder
            void AppendCell(StringBuilder sb, string value, int width, bool leftAlign = false)
            {
                sb.Append("  ");
                sb.Append(leftAlign ? value.PadRight(width) : value.PadLeft(width));
            }

            // ── Display (monospace) ───────────────────────────────────────────────
            var displaySb = new StringBuilder();

            // Header
            displaySb.Append("id".PadRight(idWidth));
            if (hasDevelopment) AppendCell(displaySb, devHeader,  devWidth,  leftAlign: true);
            if (hasMasks)       AppendCell(displaySb, maskHeader, maskWidth, leftAlign: true);
            if (hasMasks)       AppendCell(displaySb, sideHeader, sideWidth, leftAlign: true);
            foreach (string col in allColumns)
                AppendCell(displaySb, columnLabels[col], colWidths[col]);
            displaySb.AppendLine();

            // Separator
            displaySb.Append(new string('-', idWidth));
            if (hasDevelopment) displaySb.Append("  " + new string('-', devWidth));
            if (hasMasks)       displaySb.Append("  " + new string('-', maskWidth));
            if (hasMasks)       displaySb.Append("  " + new string('-', sideWidth));
            foreach (string col in allColumns)
                displaySb.Append("  " + new string('-', colWidths[col]));
            displaySb.AppendLine();

            // Rows
            for (int i = 0; i < displayRows.Count; i++)
            {
                DisplayRow r = displayRows[i];
                displaySb.Append(r.BasinId.PadRight(idWidth));
                if (hasDevelopment) AppendCell(displaySb, r.Development, devWidth,  leftAlign: true);
                if (hasMasks)       AppendCell(displaySb, r.MaskName,    maskWidth, leftAlign: true);
                if (hasMasks)       AppendCell(displaySb, r.Side,        sideWidth, leftAlign: true);
                foreach (string l in allLayers)
                {
                    string cell = r.Areas.TryGetValue(l, out double a) ? ConvertArea(a).ToString(areaFmt) : "";
                    AppendCell(displaySb, cell, colWidths[l]);
                }
                AppendCell(displaySb, ConvertArea(rowTotals[i]).ToString(areaFmt), colWidths["Total"]);
                displaySb.AppendLine();

                // Blank line after last row for each basin
                bool lastForBasin = i == displayRows.Count - 1 || displayRows[i + 1].BasinId != r.BasinId;
                if (lastForBasin && i < displayRows.Count - 1)
                    displaySb.AppendLine();
            }

            // ── Clipboard (tab-separated) ─────────────────────────────────────────
            var clipSb = new StringBuilder();

            // Header
            clipSb.Append("id");
            if (hasDevelopment) { clipSb.Append('\t'); clipSb.Append(devHeader); }
            if (hasMasks)       { clipSb.Append('\t'); clipSb.Append(maskHeader); }
            if (hasMasks)       { clipSb.Append('\t'); clipSb.Append(sideHeader); }
            foreach (string col in allColumns) { clipSb.Append('\t'); clipSb.Append(columnLabels[col]); }
            clipSb.AppendLine();

            for (int i = 0; i < displayRows.Count; i++)
            {
                DisplayRow r = displayRows[i];
                clipSb.Append($"=\"{r.BasinId}\"");   // force text in Excel
                if (hasDevelopment) { clipSb.Append('\t'); clipSb.Append(r.Development); }
                if (hasMasks)       { clipSb.Append('\t'); clipSb.Append(r.MaskName); }
                if (hasMasks)       { clipSb.Append('\t'); clipSb.Append(r.Side); }
                foreach (string l in allLayers)
                {
                    clipSb.Append('\t');
                    if (r.Areas.TryGetValue(l, out double a))
                        clipSb.Append(ConvertArea(a).ToString(areaFmt));
                }
                clipSb.Append('\t');
                clipSb.Append(ConvertArea(rowTotals[i]).ToString(areaFmt));
                clipSb.AppendLine();
            }

            // ── Step 10: Show results ─────────────────────────────────────────────
            var form = new C3DTools.UI.BasinLanduseResultForm(displaySb.ToString(), clipSb.ToString());
            Application.ShowModalDialog(form);
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
