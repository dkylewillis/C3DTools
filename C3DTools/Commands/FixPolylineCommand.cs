using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using C3DTools.Helpers;

namespace C3DTools.Commands
{
    public class FixPolylineCommand
    {
        [CommandMethod("FIXPLINE")]
        public void FixPolyline()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt user to select polylines
            var opts = new PromptSelectionOptions();
            opts.MessageForAdding = "\nSelect polylines to fix: ";

            var filter = PolylineHelper.GetPolylineFilter();
            var result = ed.GetSelection(opts, filter);
            if (result.Status != PromptStatus.OK) return;

            int fixedCount = 0;
            int skippedCount = 0;
            int multiCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (SelectedObject selObj in result.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    // Convert AutoCAD polyline to NTS geometry
                    var ntsGeom = GeometryConverter.PolylineToNts(pline);
                    if (ntsGeom == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Check if it actually needs fixing
                    var validator = new IsValidOp(ntsGeom);
                    if (validator.IsValid)
                    {
                        skippedCount++;
                        continue;
                    }

                    ed.WriteMessage($"\n  Issue: {validator.ValidationError.Message}");

                    // Fix it using library default (keepMulti = false)
                    var fixedGeometry = NetTopologySuite.Geometries.Utilities
                        .GeometryFixer.Fix(ntsGeom);

                    // Replace the original polyline with fixed geometry
                    if (fixedGeometry != null && !fixedGeometry.IsEmpty)
                    {
                        int geomCount = GeometryHelper.ReplaceWithFixedGeometry(pline, fixedGeometry, modelSpace, tr);
                        fixedCount++;
                        if (geomCount > 1)
                        {
                            multiCount++;
                            ed.WriteMessage($"\n  Split into {geomCount} geometries");
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nFIXPLINE complete: {fixedCount} fixed, {skippedCount} skipped.");
            if (multiCount > 0)
                ed.WriteMessage($"\n  {multiCount} geometries split into multiple polylines.");
        }
    }
}
