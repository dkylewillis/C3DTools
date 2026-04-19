using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using C3DTools.Helpers;

namespace C3DTools.Commands
{
    public class UnionCommand
    {
        [CommandMethod("PLUNION")]
        public void UnionPolylines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt for delete option
            var deleteOpts = new PromptKeywordOptions("\nDelete original polylines?");
            deleteOpts.Keywords.Add("Yes");
            deleteOpts.Keywords.Add("No");
            deleteOpts.Keywords.Default = "Yes";
            deleteOpts.AllowNone = true;

            var deleteResult = ed.GetKeywords(deleteOpts);
            if (deleteResult.Status != PromptStatus.OK && deleteResult.Status != PromptStatus.None)
                return;

            bool deleteOriginals = deleteResult.Status == PromptStatus.None || deleteResult.StringResult == "Yes";

            var filter = PolylineHelper.GetPolylineFilter();
            var result = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect polylines to union: " }, filter);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

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
                    ed.WriteMessage("\nNeed at least 2 valid polylines for union.");
                    return;
                }

                // Validate geometries before operation
                if (!GeometryValidator.ValidateAndFixGeometries(geometries, originals, ed, modelSpace, tr))
                {
                    return;
                }

                // Perform union
                Geometry unionResult = geometries[0];
                for (int i = 1; i < geometries.Count; i++)
                {
                    unionResult = unionResult.Union(geometries[i]);
                }

                // Create new polyline(s) from result
                int count = GeometryHelper.CreatePolylines(unionResult, originals[0], modelSpace, tr);

                // Delete originals if requested
                if (deleteOriginals)
                {
                    foreach (var pline in originals)
                    {
                        pline.UpgradeOpen();
                        pline.Erase();
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nPLUNION complete: {geometries.Count} polylines → {count} result(s).");
            }
        }
    }
}
