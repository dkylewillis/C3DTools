using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace C3DTools.Commands
{
    public class BasinCommands
    {
        private const string AppNameBasin = "C3DTools_Basin";
        private const string BasinSplitAppName = "C3DTools_BasinSplit";

        [CommandMethod("TAGBASIN")]
        public void TagBasin()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user to select a polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect basin polyline to tag: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return;

            // Prompt for basin ID
            PromptStringOptions pso = new PromptStringOptions("\nEnter basin ID: ");
            pso.AllowSpaces = true;
            PromptResult pr = ed.GetString(pso);

            if (pr.Status != PromptStatus.OK)
                return;

            string idValue = pr.StringResult;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Register app name if needed
                RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(AppNameBasin))
                {
                    rat.UpgradeOpen();
                    RegAppTableRecord ratr = new RegAppTableRecord();
                    ratr.Name = AppNameBasin;
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }

                // Open polyline and attach XData
                Polyline pline = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameBasin),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, idValue)
                );

                pline.XData = rb;
                rb.Dispose();

                tr.Commit();
                ed.WriteMessage($"\nPolyline tagged with basin ID: {idValue}");
            }
        }

        [CommandMethod("GETBASIN")]
        public void GetBasin()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user to select a polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect basin polyline to retrieve ID: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pline = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);

                ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);

                if (rb == null)
                {
                    ed.WriteMessage("\nNo basin tag found on this polyline.");
                }
                else
                {
                    TypedValue[] values = rb.AsArray();
                    if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    {
                        string? id = values[1].Value.ToString();
                        ed.WriteMessage($"\nBasin ID: {id}");
                    }
                    else
                    {
                        ed.WriteMessage("\nInvalid basin tag structure.");
                    }

                    rb.Dispose();
                }

                // Also report split classification if this is a generated split piece
                ResultBuffer? rbSplit = pline.GetXDataForApplication(BasinSplitAppName);
                if (rbSplit != null)
                {
                    TypedValue[] splitValues = rbSplit.AsArray();
                    // Payload: [RegAppName, basinId, "ONSITE"|"OFFSITE"]
                    if (splitValues.Length > 2
                        && splitValues[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString
                        && splitValues[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    {
                        string? parentId = splitValues[1].Value?.ToString();
                        string? classification = splitValues[2].Value?.ToString();
                        ed.WriteMessage($"\nSplit Classification: {classification}");
                        ed.WriteMessage($"\nParent Basin ID: {parentId}");
                    }

                    rbSplit.Dispose();
                }

                tr.Commit();
            }
        }

        [CommandMethod("LABELBASIN")]
        public void LabelBasin()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user to select a tagged basin polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect tagged basin polyline: ");
            peo.SetRejectMessage("\nMust be a polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return;

            // Retrieve basin ID from polyline
            string? idValue = null;
            ObjectId polylineOid = per.ObjectId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pline = (Polyline)tr.GetObject(polylineOid, OpenMode.ForRead);

                ResultBuffer rb = pline.GetXDataForApplication(AppNameBasin);
                if (rb == null)
                {
                    ed.WriteMessage("\nNo basin tag found on this polyline.");
                    return;
                }

                TypedValue[] values = rb.AsArray();
                if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    idValue = values[1].Value.ToString();
                }
                else
                {
                    ed.WriteMessage("\nInvalid basin tag structure.");
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
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Polyline pline = (Polyline)tr.GetObject(polylineOid, OpenMode.ForRead);

                DBText text = new DBText();
                text.Position = ppr.Value;
                text.TextString = idValue;
                text.Height = db.Textsize;
                text.Layer = pline.Layer; // Inherit polyline layer

                btr.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                tr.Commit();
                ed.WriteMessage($"\nBasin label '{idValue}' placed at {ppr.Value}");
            }
        }
    }
}
