using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Models;
using System.Collections.Generic;
using System.Globalization;

namespace C3DTools.Services
{
    /// <summary>
    /// Service layer that wraps all AutoCAD API access for basin operations.
    /// All methods are safe to call from UI thread (use DocumentLock internally).
    /// </summary>
    public class BasinDataService
    {
        private const string AppNameBasin = "C3DTools_Basin";
        private const string AppNameHydroInputs = "C3DTools_HydroInputs";

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

                    // Read basin XData: [AppName, BasinId, Development]
                    ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                    if (rb != null)
                    {
                        TypedValue[] values = rb.AsArray();

                        // Basin ID (index 1)
                        if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.BasinId = values[1].Value?.ToString();
                        }

                        // Development (index 2)
                        if (values.Length > 2 && values[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.Development = values[2].Value?.ToString() ?? string.Empty;
                        }

                        rb.Dispose();
                    }

                    ApplyHydrologyInputs(pline, basin);

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

                    // Read basin XData: [AppName, BasinId, Development]
                    ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                    if (rb != null)
                    {
                        TypedValue[] values = rb.AsArray();

                        // Basin ID (index 1)
                        if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.BasinId = values[1].Value?.ToString();
                        }

                        // Development (index 2)
                        if (values.Length > 2 && values[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            basin.Development = values[2].Value?.ToString() ?? string.Empty;
                        }

                        rb.Dispose();
                    }

                    ApplyHydrologyInputs(pline, basin);

                    tr.Commit();
                    return basin;
                }
            }
        }

        /// <summary>
        /// Tags a polyline with basin attributes (ID, Development).
        /// </summary>
        public bool TagBasin(Document doc, ObjectId polylineId, string basinId, string development)
        {
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                EnsureRegApp(doc.Database, tr, AppNameBasin);

                // Tag the polyline with all attributes
                Polyline pline = (Polyline)tr.GetObject(polylineId, OpenMode.ForWrite);
                using ResultBuffer rb = BuildMergedBasinXData(pline, basinId, development);
                pline.XData = rb;

                tr.Commit();
                return true;
            }
        }

        /// <summary>
        /// Removes the basin tag (XData) from the specified polyline.
        /// </summary>
        public bool UntagBasin(Document doc, ObjectId polylineId)
        {
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Polyline pline = (Polyline)tr.GetObject(polylineId, OpenMode.ForWrite);

                // Setting XData to a ResultBuffer containing only the AppName entry removes all data for that app
                ResultBuffer? existing = pline.GetXDataForApplication(AppNameBasin);
                if (existing == null)
                {
                    tr.Commit();
                    return false;
                }
                existing.Dispose();

                // Null out the xdata by writing only the app name — AutoCAD removes the record
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameBasin)
                );
                pline.XData = rb;
                rb.Dispose();

                tr.Commit();
                return true;
            }
        }

        public bool UpdateHydrologyInputs(Document doc, ObjectId polylineId, double? curveNumber, double? tcMinutes, string downstreamId)
        {
            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                EnsureRegApp(doc.Database, tr, AppNameHydroInputs);
                Polyline pline = (Polyline)tr.GetObject(polylineId, OpenMode.ForWrite);
                using ResultBuffer rb = BuildMergedHydrologyInputsXData(pline, curveNumber, tcMinutes, downstreamId);
                pline.XData = rb;

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
            Application.UpdateScreen();
        }

        private static void ApplyHydrologyInputs(Polyline pline, BasinInfo basin)
        {
            ResultBuffer? rb = pline.GetXDataForApplication(AppNameHydroInputs);
            if (rb == null)
                return;

            try
            {
                TypedValue[] values = rb.AsArray();
                basin.TcMinutes = ParseNullableDouble(ReadXDataString(values, 2));
                basin.DownstreamId = ReadXDataString(values, 3);
                basin.CurveNumber = ParseNullableDouble(ReadXDataString(values, 4));
            }
            finally
            {
                rb.Dispose();
            }
        }

        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(appName))
                return;

            rat.UpgradeOpen();
            RegAppTableRecord ratr = new RegAppTableRecord { Name = appName };
            rat.Add(ratr);
            tr.AddNewlyCreatedDBObject(ratr, true);
        }

        private static ResultBuffer BuildMergedBasinXData(Polyline pline, string basinId, string development)
        {
            var values = ExistingXDataWithout(pline, AppNameBasin);
            values.AddRange(new[]
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameBasin),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, basinId),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, development)
            });
            return new ResultBuffer(values.ToArray());
        }

        private static ResultBuffer BuildMergedHydrologyInputsXData(Polyline pline, double? curveNumber, double? tcMinutes, string downstreamId)
        {
            var values = ExistingXDataWithout(pline, AppNameHydroInputs);
            values.AddRange(new[]
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameHydroInputs),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(tcMinutes)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, ToXDataString(downstreamId)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, FormatNumber(curveNumber))
            });
            return new ResultBuffer(values.ToArray());
        }

        private static List<TypedValue> ExistingXDataWithout(Polyline pline, string appName)
        {
            var values = new List<TypedValue>();
            ResultBuffer? existing = pline.XData;
            if (existing == null)
                return values;

            try
            {
                values.AddRange(RemoveRegAppGroup(existing.AsArray(), appName));
            }
            finally
            {
                existing.Dispose();
            }

            return values;
        }

        private static IEnumerable<TypedValue> RemoveRegAppGroup(TypedValue[] values, string appName)
        {
            bool skipCurrentGroup = false;
            foreach (TypedValue value in values)
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                    skipCurrentGroup = string.Equals(value.Value?.ToString(), appName, System.StringComparison.OrdinalIgnoreCase);

                if (!skipCurrentGroup)
                    yield return value;
            }
        }

        private static string ReadXDataString(TypedValue[] values, int index)
        {
            if (values.Length <= index || values[index].TypeCode != (int)DxfCode.ExtendedDataAsciiString)
                return string.Empty;

            return values[index].Value?.ToString() ?? string.Empty;
        }

        private static double? ParseNullableDouble(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : null;
        }

        private static string FormatNumber(double? value) => value.HasValue
            ? value.Value.ToString("0.######", CultureInfo.InvariantCulture)
            : string.Empty;

        private static string ToXDataString(string? value)
        {
            const int MaxXDataStringLength = 255;
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= MaxXDataStringLength ? value : value.Substring(0, MaxXDataStringLength);
        }
    }
}
