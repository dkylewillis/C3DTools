using Autodesk.AutoCAD.DatabaseServices;
using C3DTools.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace C3DTools.Services
{
    /// <summary>
    /// Persists Hydraflow-style hydrograph row definitions in the drawing's
    /// Named Object Dictionary under "C3DTools_Hydrographs".
    /// </summary>
    public class HydrologyHydrographService
    {
        private const string NodKey = "C3DTools_Hydrographs";
        private const string InflowDelimiter = ",";

        public List<HydrologyHydrograph> GetHydrographs(Database db)
        {
            var rows = new List<HydrologyHydrograph>();

            using Transaction tr = db.TransactionManager.StartTransaction();
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NodKey))
            {
                tr.Commit();
                return rows;
            }

            DBDictionary hydrographDict = (DBDictionary)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForRead);
            foreach (DictionaryEntry entry in hydrographDict)
            {
                if (entry.Value is not ObjectId objectId)
                    continue;

                Xrecord xrec = (Xrecord)tr.GetObject(objectId, OpenMode.ForRead);
                HydrologyHydrograph? row = ReadXrecord(xrec);
                if (row != null)
                    rows.Add(row);
            }

            tr.Commit();

            return rows
                .OrderBy(row => ParseHydrographNumber(row.Id))
                .ThenBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void SaveHydrographs(Database db, IEnumerable<HydrologyHydrograph> hydrographs)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            DBDictionary hydrographDict;
            if (nod.Contains(NodKey))
            {
                hydrographDict = (DBDictionary)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForWrite);
                var existingKeys = hydrographDict
                    .Cast<DictionaryEntry>()
                    .Select(entry => entry.Key?.ToString())
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => key!)
                    .ToList();
                foreach (string key in existingKeys)
                {
                    DBObject obj = tr.GetObject(hydrographDict.GetAt(key), OpenMode.ForWrite);
                    obj.Erase();
                    hydrographDict.Remove(key);
                }
            }
            else
            {
                hydrographDict = new DBDictionary();
                nod.SetAt(NodKey, hydrographDict);
                tr.AddNewlyCreatedDBObject(hydrographDict, true);
            }

            foreach (HydrologyHydrograph row in hydrographs.Where(row => !string.IsNullOrWhiteSpace(row.Id)))
            {
                Xrecord xrec = new Xrecord { Data = BuildResultBuffer(row) };
                hydrographDict.SetAt(row.Id, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            tr.Commit();
        }

        private static HydrologyHydrograph? ReadXrecord(Xrecord xrec)
        {
            TypedValue[] values = xrec.Data.AsArray();
            if (values.Length < 6)
                return null;

            var row = new HydrologyHydrograph
            {
                Id = values[0].Value?.ToString() ?? string.Empty,
                Type = values[1].Value?.ToString() ?? "SCS",
                BasinId = values[2].Value?.ToString() ?? string.Empty,
                InflowIds = SplitInflows(values[3].Value?.ToString() ?? string.Empty),
                DownstreamId = values[4].Value?.ToString() ?? string.Empty,
                Description = values[5].Value?.ToString() ?? string.Empty
            };

            if (values.Length > 6)
            {
                try
                {
                    string parameterJson = string.Concat(values.Skip(6).Select(value => value.Value?.ToString() ?? string.Empty));
                    row.Parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parameterJson) ?? new Dictionary<string, object>();
                }
                catch
                {
                    row.Parameters = new Dictionary<string, object>();
                }
            }

            return row;
        }

        private static ResultBuffer BuildResultBuffer(HydrologyHydrograph row)
        {
            var values = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Text, ToRecordString(row.Id)),
                new TypedValue((int)DxfCode.Text, ToRecordString(row.Type)),
                new TypedValue((int)DxfCode.Text, ToRecordString(row.BasinId)),
                new TypedValue((int)DxfCode.Text, ToRecordString(string.Join(InflowDelimiter, row.InflowIds))),
                new TypedValue((int)DxfCode.Text, ToRecordString(row.DownstreamId)),
                new TypedValue((int)DxfCode.Text, ToRecordString(row.Description))
            };

            foreach (string chunk in SplitRecordString(JsonSerializer.Serialize(row.Parameters)))
                values.Add(new TypedValue((int)DxfCode.Text, chunk));

            return new ResultBuffer(values.ToArray());
        }

        private static IEnumerable<string> SplitRecordString(string? value)
        {
            const int MaxXrecordTextLength = 255;
            if (string.IsNullOrEmpty(value))
            {
                yield return string.Empty;
                yield break;
            }

            for (int index = 0; index < value.Length; index += MaxXrecordTextLength)
                yield return value.Substring(index, Math.Min(MaxXrecordTextLength, value.Length - index));
        }

        private static List<string> SplitInflows(string value)
        {
            return value
                .Split(new[] { InflowDelimiter }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private static int ParseHydrographNumber(string id)
        {
            if (id.Length > 1 && id[0] == 'H' && int.TryParse(id.Substring(1), out int number))
                return number;
            return int.MaxValue;
        }

        private static string ToRecordString(string? value)
        {
            const int MaxXrecordTextLength = 255;
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= MaxXrecordTextLength ? value : value.Substring(0, MaxXrecordTextLength);
        }
    }
}
