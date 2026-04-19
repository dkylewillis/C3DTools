using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace C3DTools.Commands
{
    public class FixHatchCommand
    {
        [CommandMethod("FIXHATCH")]
        public void FixHatch()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            // -- 1. Select hatches ------------------------------------------------
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect hatches to fix: ";

            SelectionFilter filter = new SelectionFilter(new TypedValue[]
            {
                new TypedValue((int)DxfCode.Start, "HATCH")
            });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo hatches selected.");
                return;
            }

            int fixedCount   = 0;
            int skippedCount = 0;
            int errorCount   = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                foreach (SelectedObject selObj in psr.Value)
                {
                    Hatch original = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Hatch;
                    if (original == null) continue;

                    // -- 2. Extract and validate geometry -------------------------
                    Geometry ntsGeom;
                    try { ntsGeom = GeometryConverter.HatchToNts(original); }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\nHatch {original.Handle}: conversion error -- {ex.Message}");
                        errorCount++;
                        continue;
                    }

                    if (ntsGeom == null || ntsGeom.IsEmpty)
                    {
                        ed.WriteMessage($"\nHatch {original.Handle}: could not extract geometry, skipping.");
                        errorCount++;
                        continue;
                    }

                    if (new IsValidOp(ntsGeom).IsValid)
                    {
                        skippedCount++;
                        continue;
                    }

                    // -- 3. Delegate fix to HatchFixer ----------------------------
                    Geometry result = HatchFixer.TryFixHatch(original, tr, btr, ed);
                    if (result != null)
                        fixedCount++;
                    else
                        errorCount++;
                }

                tr.Commit();
            }

            ed.WriteMessage(
                $"\n\nFIXHATCH complete: {fixedCount} fixed, {skippedCount} already valid, {errorCount} error(s).");
        }
    }
}
