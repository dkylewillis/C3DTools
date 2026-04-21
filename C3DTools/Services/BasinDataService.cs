using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Models;
using System.Collections.Generic;

namespace C3DTools.Services
{
    /// <summary>
    /// Service layer that wraps all AutoCAD API access for basin operations.
    /// All methods are safe to call from UI thread (use DocumentLock internally).
    /// </summary>
    public class BasinDataService
    {
        private const string AppNameBasin = "C3DTools_Basin";

        /// <summary>
        /// Gets all basin polylines (both tagged and untagged) in the current drawing.
        /// </summary>
        public List<BasinInfo> GetAllBasins(Document doc)
        {
            var basins = new List<BasinInfo>();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;

                    Polyline pline = (Polyline)tr.GetObject(oid, OpenMode.ForRead);
                    if (!pline.Closed)
                        continue;

                    var basin = new BasinInfo
                    {
                        ObjectId = oid,
                        Layer = pline.Layer
                    };

                    // Read basin XData: [AppName, BasinId, Boundary, Development]
                    ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                    if (rb != null)
                    {
                        TypedValue[] values = rb.AsArray();

                        // Basin ID (index 1)
                        if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.BasinId = values[1].Value?.ToString();
                        }

                        // Boundary (index 2)
                        if (values.Length > 2 && values[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.Boundary = values[2].Value?.ToString() ?? string.Empty;
                        }

                        // Development (index 3)
                        if (values.Length > 3 && values[3].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.Development = values[3].Value?.ToString() ?? string.Empty;
                        }

                        rb.Dispose();
                    }

                    basins.Add(basin);
                }

                tr.Commit();
            }

            return basins;
        }

        /// <summary>
        /// Gets basin info for the currently selected entity (if it's a closed polyline).
        /// </summary>
        public BasinInfo? GetSelectedBasin(Document doc)
        {
            using (DocumentLock docLock = doc.LockDocument())
            {
                Editor ed = doc.Editor;
                PromptSelectionResult psr = ed.SelectImplied();

                if (psr.Status != PromptStatus.OK)
                    return null;

                SelectionSet ss = psr.Value;
                if (ss.Count != 1)
                    return null;

                ObjectId oid = ss[0].ObjectId;

                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        return null;

                    Polyline pline = (Polyline)tr.GetObject(oid, OpenMode.ForRead);
                    if (!pline.Closed)
                        return null;

                    var basin = new BasinInfo
                    {
                        ObjectId = oid,
                        Layer = pline.Layer
                    };

                    // Read basin XData: [AppName, BasinId, Boundary, Development]
                    ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                    if (rb != null)
                    {
                        TypedValue[] values = rb.AsArray();

                        // Basin ID (index 1)
                        if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.BasinId = values[1].Value?.ToString();
                        }

                        // Boundary (index 2)
                        if (values.Length > 2 && values[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.Boundary = values[2].Value?.ToString() ?? string.Empty;
                        }

                        // Development (index 3)
                        if (values.Length > 3 && values[3].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.Development = values[3].Value?.ToString() ?? string.Empty;
                        }

                        rb.Dispose();
                    }

                    tr.Commit();
                    return basin;
                }
            }
        }

        /// <summary>
        /// Tags a polyline with basin attributes (ID, Boundary, Development).
        /// </summary>
        public bool TagBasin(Document doc, ObjectId polylineId, string basinId, string boundary, string development)
        {
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                // Register app name if needed
                RegAppTable rat = (RegAppTable)tr.GetObject(doc.Database.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(AppNameBasin))
                {
                    rat.UpgradeOpen();
                    RegAppTableRecord ratr = new RegAppTableRecord { Name = AppNameBasin };
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }

                // Tag the polyline with all attributes
                Polyline pline = (Polyline)tr.GetObject(polylineId, OpenMode.ForWrite);
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameBasin),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, basinId),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, boundary),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, development)
                );

                pline.XData = rb;
                rb.Dispose();

                tr.Commit();
                return true;
            }
        }

        /// <summary>
        /// Selects the specified basin polyline in the editor.
        /// </summary>
        public void SelectBasin(Document doc, ObjectId polylineId)
        {
            Editor ed = doc.Editor;
            ObjectId[] ids = new ObjectId[] { polylineId };
            ed.SetImpliedSelection(ids);
        }
    }
}
