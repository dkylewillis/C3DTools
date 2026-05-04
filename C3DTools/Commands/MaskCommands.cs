using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Infrastructure;

namespace C3DTools.Commands
{
    public class MaskCommands
    {
        /// <summary>
        /// Prompts the user to pick a closed polyline and stores its handle in
        /// <see cref="MaskPickState"/> for the Masks palette tab to consume.
        /// </summary>
        [CommandMethod("MASKPICK")]
        public void MaskPick()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect mask polyline: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using Transaction tr = doc.Database.TransactionManager.StartTransaction();
            Polyline pline = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);

            if (!pline.Closed)
            {
                ed.WriteMessage("\nPolyline must be closed to be used as a mask.");
                tr.Commit();
                return;
            }

            MaskPickState.PendingHandle = pline.Handle.ToString();
            ed.WriteMessage($"\nMask polyline selected (handle: {MaskPickState.PendingHandle})");
            tr.Commit();
        }
    }
}
