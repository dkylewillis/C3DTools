using Autodesk.AutoCAD.DatabaseServices;
using C3DTools.Models;
using System;
using System.Collections.Generic;

namespace C3DTools.Services
{
    /// <summary>
    /// Persists and loads <see cref="MaskDefinition"/> entries in the drawing's
    /// Named Object Dictionary under "C3DTools_Masks".
    ///
    /// Storage layout:
    ///   NOD["C3DTools_Masks"]  → DBDictionary
    ///     [mask.Name]          → Xrecord
    ///       [0] Text  = Name
    ///       [1] Text  = PolylineHandle
    ///       [2] Text  = InsideLabel
    ///       [3] Text  = OutsideLabel
    ///       [4] Text  = LayerPrefix
    ///       [5] Int16 = GenerateGeometry (0 = false, 1 = true)
    /// </summary>
    public class MaskService
    {
        private const string NodKey = "C3DTools_Masks";

        // ── Public API ───────────────────────────────────────────────────────────

        public List<MaskDefinition> GetMasks(Database db)
        {
            var results = new List<MaskDefinition>();

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(NodKey))
                {
                    tr.Commit();
                    return results;
                }

                DBDictionary maskDict = (DBDictionary)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForRead);

                foreach (DBDictionaryEntry entry in maskDict)
                {
                    Xrecord xrec = (Xrecord)tr.GetObject(entry.Value, OpenMode.ForRead);
                    MaskDefinition? mask = ReadXrecord(xrec);
                    if (mask != null)
                        results.Add(mask);
                }

                tr.Commit();
            }
            catch { /* best-effort */ }

            return results;
        }

        public void SaveMask(Database db, MaskDefinition mask)
        {
            if (string.IsNullOrWhiteSpace(mask.Name))
                throw new ArgumentException("Mask name cannot be empty.", nameof(mask));

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                DBDictionary maskDict;
                if (nod.Contains(NodKey))
                {
                    maskDict = (DBDictionary)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForWrite);
                }
                else
                {
                    maskDict = new DBDictionary();
                    nod.SetAt(NodKey, maskDict);
                    tr.AddNewlyCreatedDBObject(maskDict, true);
                }

                Xrecord xrec;
                if (maskDict.Contains(mask.Name))
                {
                    xrec = (Xrecord)tr.GetObject(maskDict.GetAt(mask.Name), OpenMode.ForWrite);
                }
                else
                {
                    xrec = new Xrecord();
                    maskDict.SetAt(mask.Name, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }

                xrec.Data = BuildResultBuffer(mask);
                tr.Commit();
            }
            catch { /* best-effort */ }
        }

        public void DeleteMask(Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(NodKey))
                {
                    tr.Commit();
                    return;
                }

                DBDictionary maskDict = (DBDictionary)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForWrite);

                if (maskDict.Contains(name))
                {
                    DBObject obj = tr.GetObject(maskDict.GetAt(name), OpenMode.ForWrite);
                    obj.Erase();
                    maskDict.Remove(name);
                }

                tr.Commit();
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Resolves the mask polyline's ObjectId from its stored handle string.
        /// Returns <see cref="ObjectId.Null"/> if not found.
        /// </summary>
        public ObjectId ResolvePolyline(Database db, string handle)
        {
            try
            {
                long ln = Convert.ToInt64(handle, 16);
                Handle h = new Handle(ln);
                if (db.TryGetObjectId(h, out ObjectId id))
                    return id;
            }
            catch { }

            return ObjectId.Null;
        }

        /// <summary>
        /// Returns <c>true</c> if the polyline handle resolves to a live (non-erased) object.
        /// </summary>
        public bool IsPolylineValid(Database db, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return false;
            try
            {
                long ln = Convert.ToInt64(handle, 16);
                Handle h = new Handle(ln);
                if (db.TryGetObjectId(h, out ObjectId id))
                    return !id.IsNull && !id.IsErased;
            }
            catch { }

            return false;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static MaskDefinition? ReadXrecord(Xrecord xrec)
        {
            TypedValue[] vals = xrec.Data.AsArray();
            if (vals.Length < 6) return null;

            return new MaskDefinition
            {
                Name             = vals[0].Value?.ToString() ?? string.Empty,
                PolylineHandle   = vals[1].Value?.ToString() ?? string.Empty,
                InsideLabel      = vals[2].Value?.ToString() ?? "IN",
                OutsideLabel     = vals[3].Value?.ToString() ?? "OUT",
                LayerPrefix      = vals[4].Value?.ToString() ?? "CALC-BASN",
                GenerateGeometry = vals[5].Value is short s && s == 1
            };
        }

        private static ResultBuffer BuildResultBuffer(MaskDefinition mask)
        {
            return new ResultBuffer(
                new TypedValue((int)DxfCode.Text, mask.Name),
                new TypedValue((int)DxfCode.Text, mask.PolylineHandle),
                new TypedValue((int)DxfCode.Text, mask.InsideLabel),
                new TypedValue((int)DxfCode.Text, mask.OutsideLabel),
                new TypedValue((int)DxfCode.Text, mask.LayerPrefix),
                new TypedValue((int)DxfCode.Int16, (short)(mask.GenerateGeometry ? 1 : 0))
            );
        }
    }
}
