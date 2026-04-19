using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace C3DTools.Commands
{
    public class HatchOverlapCommand
    {
        private const string AppName = "C3DTools_ID";

        [CommandMethod("HATCHOVERLAP")]
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
            var polylineGeoms = new Dictionary<string, Geometry>();
            var hatchGeoms = new List<Geometry>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Convert polylines
                foreach (ObjectId oid in polylineIds)
                {
                    Polyline pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;

                    // Get ID from XData
                    string id = GetPolylineId(pline);

                    if (string.IsNullOrEmpty(id))
                    {
                        ed.WriteMessage($"\nWarning: Polyline {oid.Handle} has no ID tag. Skipping.");
                        continue;
                    }

                    Geometry geom = GeometryConverter.PolylineToNts(pline);
                    if (geom != null && geom.IsValid)
                    {
                        polylineGeoms[id] = geom;
                    }
                    else
                    {
                        ed.WriteMessage($"\nWarning: Polyline '{id}' has invalid geometry. Skipping.");
                    }
                }

                // Convert hatches
                foreach (ObjectId oid in hatchIds)
                {
                    Hatch hatch = tr.GetObject(oid, OpenMode.ForRead) as Hatch;

                    Geometry geom = GeometryConverter.HatchToNts(hatch);
                    if (geom != null && geom.IsValid)
                    {
                        hatchGeoms.Add(geom);
                    }
                    else
                    {
                        ed.WriteMessage($"\nWarning: Hatch {oid.Handle} has invalid geometry. Skipping.");
                    }
                }

                tr.Commit();
            }

            // Step 4: Compute overlap areas
            ed.WriteMessage("\n--- Overlap Analysis Results ---");

            foreach (var kvp in polylineGeoms)
            {
                string polylineId = kvp.Key;
                Geometry polylineGeom = kvp.Value;

                ed.WriteMessage($"\nPolyline ID: {polylineId}");

                for (int i = 0; i < hatchGeoms.Count; i++)
                {
                    Geometry hatchGeom = hatchGeoms[i];

                    Geometry intersection = polylineGeom.Intersection(hatchGeom);

                    if (intersection != null && !intersection.IsEmpty)
                    {
                        double area = intersection.Area;
                        ed.WriteMessage($"\n  Hatch {i + 1}: Overlap Area = {area:F4}");
                    }
                    else
                    {
                        ed.WriteMessage($"\n  Hatch {i + 1}: No overlap");
                    }
                }
            }

            ed.WriteMessage("\n--- End of Analysis ---");
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
