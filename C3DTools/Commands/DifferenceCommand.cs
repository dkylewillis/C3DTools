using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using C3DTools.Helpers;

namespace C3DTools.Commands
{
    public class DifferenceCommand
    {
        [CommandMethod("PLDIFF")]
        public void DifferencePolylines()
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

            // Select base polyline
            var baseResult = ed.GetEntity(new PromptEntityOptions("\nSelect base polyline: ") { AllowNone = false });
            if (baseResult.Status != PromptStatus.OK) return;

            // Select polylines to subtract
            var subtractResult = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect polylines to subtract: " }, filter);
            if (subtractResult.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Get base geometry
                var basePline = tr.GetObject(baseResult.ObjectId, OpenMode.ForRead) as Polyline;
                if (basePline == null)
                {
                    ed.WriteMessage("\nBase entity is not a polyline.");
                    return;
                }

                var baseGeom = GeometryConverter.PolylineToNts(basePline);
                if (baseGeom == null)
                {
                    ed.WriteMessage("\nInvalid base polyline.");
                    return;
                }

                // Collect all geometries and polylines for validation
                var allGeometries = new List<Geometry> { baseGeom };
                var allPolylines = new List<Polyline> { basePline };

                // Subtract each selected polyline
                var toSubtract = new List<Polyline>();
                foreach (SelectedObject selObj in subtractResult.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null || pline.ObjectId == basePline.ObjectId) continue;

                    var ntsGeom = GeometryConverter.PolylineToNts(pline);
                    if (ntsGeom != null)
                    {
                        allGeometries.Add(ntsGeom);
                        allPolylines.Add(pline);
                        toSubtract.Add(pline);
                    }
                }

                // Validate geometries before operation
                if (!GeometryValidator.ValidateAndFixGeometries(allGeometries, allPolylines, ed, modelSpace, tr))
                {
                    return;
                }

                // Update baseGeom after potential fixes
                baseGeom = allGeometries[0];

                // Perform difference operations
                for (int i = 1; i < allGeometries.Count; i++)
                {
                    baseGeom = baseGeom.Difference(allGeometries[i]);
                }

                if (baseGeom.IsEmpty)
                {
                    ed.WriteMessage("\nResult is empty - complete subtraction.");
                    if (deleteOriginals)
                    {
                        basePline.UpgradeOpen();
                        basePline.Erase();
                    }
                }
                else
                {
                    // Create result polyline(s)
                    int count = GeometryHelper.CreatePolylines(baseGeom, basePline, modelSpace, tr);
                    ed.WriteMessage($"\nPLDIFF complete: {count} result(s).");

                    // Delete base if requested
                    if (deleteOriginals)
                    {
                        basePline.UpgradeOpen();
                        basePline.Erase();
                    }
                }

                tr.Commit();
            }
        }
    }
}
