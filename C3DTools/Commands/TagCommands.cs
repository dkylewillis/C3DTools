using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace C3DTools.Commands
{
    public class TagCommands
    {
        private const string AppName = "C3DTools_ID";

        [CommandMethod("TAGID")]
        public void TagId()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user to select a polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect polyline to tag: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return;

            // Prompt for string ID
            PromptStringOptions pso = new PromptStringOptions("\nEnter ID string: ");
            pso.AllowSpaces = true;
            PromptResult pr = ed.GetString(pso);

            if (pr.Status != PromptStatus.OK)
                return;

            string idValue = pr.StringResult;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Register app name if needed
                RegAppTable rat = tr.GetObject(db.RegAppTableId, OpenMode.ForRead) as RegAppTable;
                if (!rat.Has(AppName))
                {
                    rat.UpgradeOpen();
                    RegAppTableRecord ratr = new RegAppTableRecord();
                    ratr.Name = AppName;
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }

                // Open polyline and attach XData
                Polyline pline = tr.GetObject(per.ObjectId, OpenMode.ForWrite) as Polyline;

                // Build XData ResultBuffer
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, idValue)
                );

                pline.XData = rb;
                rb.Dispose();

                tr.Commit();
                ed.WriteMessage($"\nPolyline tagged with ID: {idValue}");
            }
        }

        [CommandMethod("GETID")]
        public void GetId()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user to select a polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect polyline to retrieve ID: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pline = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;

                ResultBuffer rb = pline.GetXDataForApplication(AppName);

                if (rb == null)
                {
                    ed.WriteMessage("\nNo ID tag found on this polyline.");
                }
                else
                {
                    TypedValue[] values = rb.AsArray();
                    if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    {
                        string id = values[1].Value.ToString();
                        ed.WriteMessage($"\nPolyline ID: {id}");
                    }
                    else
                    {
                        ed.WriteMessage("\nInvalid ID tag structure.");
                    }

                    rb.Dispose();
                }

                tr.Commit();
            }
        }

        [CommandMethod("LABELID")]
        public void LabelId()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user to select a tagged polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect tagged polyline: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return;

            // Retrieve ID from polyline
            string idValue = null;
            ObjectId polylineOid = per.ObjectId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pline = tr.GetObject(polylineOid, OpenMode.ForRead) as Polyline;

                ResultBuffer rb = pline.GetXDataForApplication(AppName);
                if (rb == null)
                {
                    ed.WriteMessage("\nNo ID tag found on this polyline.");
                    return;
                }

                TypedValue[] values = rb.AsArray();
                if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    idValue = values[1].Value.ToString();
                }
                else
                {
                    ed.WriteMessage("\nInvalid ID tag structure.");
                    rb.Dispose();
                    return;
                }

                rb.Dispose();
                tr.Commit();
            }

            // Prompt for insertion point
            PromptPointOptions ppo = new PromptPointOptions("\nPick point for label: ");
            PromptPointResult ppr = ed.GetPoint(ppo);

            if (ppr.Status != PromptStatus.OK)
                return;

            // Place DBText at that location
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                Polyline pline = tr.GetObject(polylineOid, OpenMode.ForRead) as Polyline;

                DBText text = new DBText();
                text.Position = ppr.Value;
                text.TextString = idValue;
                text.Height = db.Textsize;
                text.Layer = pline.Layer; // Inherit polyline layer

                btr.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                tr.Commit();
                ed.WriteMessage($"\nLabel '{idValue}' placed at {ppr.Value}");
            }
        }
    }
}
