using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using C3DTools.Helpers;

namespace C3DTools.Commands
{
    public class IntersectionCommand
    {
        [CommandMethod("PLINT")]
        public void IntersectPolylines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var filter = PolylineHelper.GetPolylineFilter();
            var result = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect polylines to intersect: " }, filter);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Convert all selected polylines to NTS geometries
                var geometries = new List<Geometry>();
                var originals = new List<Polyline>();

                foreach (SelectedObject selObj in result.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    var ntsGeom = GeometryConverter.PolylineToNts(pline);
                    if (ntsGeom != null)
                    {
                        geometries.Add(ntsGeom);
                        originals.Add(pline);
                    }
                }

                if (geometries.Count < 2)
                {
                    ed.WriteMessage("\nNeed at least 2 valid polylines for intersection.");
                    return;
                }

                // Validate geometries before operation
                if (!GeometryValidator.ValidateAndFixGeometries(geometries, ed))
                {
                    return;
                }

                // Perform intersection
                Geometry intersectResult = geometries[0];
                for (int i = 1; i < geometries.Count; i++)
                {
                    intersectResult = BooleanOperationHelper.Intersect(intersectResult, geometries[i]);
                    if (intersectResult.IsEmpty)
                    {
                        ed.WriteMessage("\nNo intersection found.");
                        return;
                    }
                }

                // Create new polyline(s) from result
                int count = GeometryHelper.CreatePolylines(intersectResult, originals[0], modelSpace, tr);

                tr.Commit();
                ed.WriteMessage($"\nPLINT complete: {count} result(s).");
            }
        }
    }
}
