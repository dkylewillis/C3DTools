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
    public class HatchOverlapCommand
    {
        private const string AppName = "C3DTools_ID";

        [CommandMethod("HATCHOVERLAPANALYSIS")]
        public void HatchOverlap()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Step 1: Select polylines
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect polylines with ID tags: ";

            SelectionFilter filter = new SelectionFilter(new TypedValue[] {
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
            });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo polylines selected.");
                return;
            }

            ObjectId[] polylineIds = psr.Value.GetObjectIds();

            // Step 2: Select hatches
            pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect hatches: ";

            filter = new SelectionFilter(new TypedValue[] {
                new TypedValue((int)DxfCode.Start, "HATCH")
            });

            psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo hatches selected.");
                return;
            }

            ObjectId[] hatchIds = psr.Value.GetObjectIds();

            // Step 3: Convert polylines and hatches to NTS geometries
            var polylineOrder = new List<string>();
            var polylineGeoms = new Dictionary<string, Geometry>();
            var hatchGeoms = new List<(Geometry Geom, string Layer)>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId oid in polylineIds)
                {
                    Polyline pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                    string id = GetPolylineId(pline);

                    if (string.IsNullOrEmpty(id))
                    {
                        ed.WriteMessage($"\nWarning: Polyline {oid.Handle} has no ID tag. Skipping.");
                        continue;
                    }

                    Geometry geom = GeometryConverter.PolylineToNts(pline);
                    if (geom != null && geom.IsValid)
                    {
                        if (!polylineGeoms.ContainsKey(id))
                            polylineOrder.Add(id);
                        polylineGeoms[id] = geom;
                    }
                    else
                    {
                        ed.WriteMessage($"\nWarning: Polyline '{id}' has invalid geometry. Skipping.");
                    }
                }

                foreach (ObjectId oid in hatchIds)
                {
                    Hatch hatch = tr.GetObject(oid, OpenMode.ForRead) as Hatch;
                    string layer = hatch.Layer ?? "0";

                    Geometry geom = GeometryConverter.HatchToNts(hatch);
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
                                tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                            Geometry fixedGeom = HatchFixer.TryFixHatch(hatch, tr, btr, ed);
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

            // Step 4: Compute overlap areas grouped by layer
            var results = new Dictionary<string, Dictionary<string, double>>();
            foreach (string id in polylineOrder)
                results[id] = new Dictionary<string, double>();

            var allLayers = new List<string>();

            foreach (var (hatchGeom, layer) in hatchGeoms)
            {
                if (!allLayers.Contains(layer))
                    allLayers.Add(layer);

                foreach (string id in polylineOrder)
                {
                    Geometry intersection = polylineGeoms[id].Intersection(hatchGeom);
                    if (intersection != null && !intersection.IsEmpty && intersection.Area > 0)
                    {
                        if (!results[id].ContainsKey(layer))
                            results[id][layer] = 0;
                        results[id][layer] += intersection.Area;
                    }
                }
            }

            allLayers.Sort();

            const string areaFmt = "F4";

            // Pre-calculate row totals
            var rowTotals = new Dictionary<string, double>();
            foreach (string id in polylineOrder)
            {
                double total = 0;
                foreach (string layer in allLayers)
                    if (results[id].TryGetValue(layer, out double a))
                        total += a;
                rowTotals[id] = total;
            }

            var allColumns = new List<string>(allLayers) { "Total" };

            // Column widths for aligned monospace display
            int idWidth = polylineOrder.Concat(new[] { "id" }).Max(s => s.Length);
            var colWidths = new Dictionary<string, int>();
            foreach (string col in allColumns)
                colWidths[col] = col.Length;
            foreach (string id in polylineOrder)
            {
                foreach (string layer in allLayers)
                    if (results[id].TryGetValue(layer, out double a))
                        colWidths[layer] = System.Math.Max(colWidths[layer], a.ToString(areaFmt).Length);
                colWidths["Total"] = System.Math.Max(colWidths["Total"], rowTotals[id].ToString(areaFmt).Length);
            }

            // Space-padded display text
            var displaySb = new StringBuilder();
            displaySb.Append("id".PadRight(idWidth));
            foreach (string col in allColumns)
                displaySb.Append("  " + col.PadLeft(colWidths[col]));
            displaySb.AppendLine();
            displaySb.Append(new string('-', idWidth));
            foreach (string col in allColumns)
                displaySb.Append("  " + new string('-', colWidths[col]));
            displaySb.AppendLine();
            foreach (string id in polylineOrder)
            {
                displaySb.Append(id.PadRight(idWidth));
                foreach (string layer in allLayers)
                {
                    string cell = results[id].TryGetValue(layer, out double a) ? a.ToString(areaFmt) : "";
                    displaySb.Append("  " + cell.PadLeft(colWidths[layer]));
                }
                displaySb.Append("  " + rowTotals[id].ToString(areaFmt).PadLeft(colWidths["Total"]));
                displaySb.AppendLine();
            }

            // Tab-separated clipboard text
            var clipSb = new StringBuilder();
            clipSb.Append("id");
            foreach (string col in allColumns) { clipSb.Append("\t"); clipSb.Append(col); }
            clipSb.AppendLine();
            foreach (string id in polylineOrder)
            {
                clipSb.Append(id);
                foreach (string layer in allLayers)
                {
                    clipSb.Append("\t");
                    if (results[id].TryGetValue(layer, out double ea))
                        clipSb.Append(ea.ToString(areaFmt));
                }
                clipSb.Append("\t");
                clipSb.Append(rowTotals[id].ToString(areaFmt));
                clipSb.AppendLine();
            }

            // Step 5: Show results dialog
            var form = new C3DTools.UI.HatchOverlapResultForm(displaySb.ToString(), clipSb.ToString());
            Application.ShowModalDialog(form);
        }

        /// <summary>
        /// Retrieves the ID tag from a polyline's XData.
        /// Returns the ID string or null if not found.
        /// </summary>
        private string GetPolylineId(Polyline pline)
        {
            ResultBuffer rb = pline.GetXDataForApplication(AppName);

            if (rb == null)
                return null;

            TypedValue[] values = rb.AsArray();
            if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
            {
                string id = values[1].Value.ToString();
                rb.Dispose();
                return id;
            }

            rb.Dispose();
            return null;
        }
    }
}
