using Autodesk.AutoCAD.DatabaseServices;
using C3DTools.Models;
using System;
using System.Collections.Generic;

namespace C3DTools.Services
{
    /// <summary>
    /// Persists and loads BasinSettings in the drawing's Named Object Dictionary (NOD)
    /// as an XRecord under the key "C3DTools_BasinSettings".
    /// </summary>
    public class DrawingSettingsService
    {
        private const string NodKey = "C3DTools_BasinSettings";
        private const string LayerDelimiter = "|";

        /// <summary>
        /// Loads per-drawing settings. Returns null if no settings are saved in the drawing.
        /// </summary>
        public BasinSettings? Load(Database db)
        {
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(NodKey))
                {
                    tr.Commit();
                    return null;
                }

                Xrecord xrec = (Xrecord)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForRead);
                TypedValue[] values = xrec.Data.AsArray();
                tr.Commit();

                if (values.Length < 1)
                    return null;

                string layersRaw = values[0].Value?.ToString() ?? string.Empty;
                AreaUnit areaUnit = AreaUnit.SquareFeet;
                if (values.Length > 1 && values[1].Value?.ToString() == nameof(AreaUnit.Acres))
                    areaUnit = AreaUnit.Acres;
                string stormDistribution = values.Length > 2
                    ? values[2].Value?.ToString() ?? "ATLAS14_ALTERNATING_BLOCK"
                    : "ATLAS14_ALTERNATING_BLOCK";
                int hydrographTimeStepMinutes = 2;
                if (values.Length > 3 && int.TryParse(values[3].Value?.ToString(), out int storedTimeStep) && storedTimeStep > 0)
                    hydrographTimeStepMinutes = storedTimeStep;

                var layers = new List<string>();
                if (!string.IsNullOrEmpty(layersRaw))
                {
                    foreach (string l in layersRaw.Split(new[] { LayerDelimiter }, StringSplitOptions.RemoveEmptyEntries))
                        layers.Add(l);
                }

                return new BasinSettings
                {
                    LanduseHatchLayers = layers,
                    AreaUnit = areaUnit,
                    StormDistribution = string.IsNullOrWhiteSpace(stormDistribution) ? "ATLAS14_ALTERNATING_BLOCK" : stormDistribution,
                    HydrographTimeStepMinutes = hydrographTimeStepMinutes
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves per-drawing settings to the drawing's Named Object Dictionary.
        /// Opens its own transaction.
        /// </summary>
        public void Save(Database db, BasinSettings settings)
        {
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                Xrecord xrec;
                if (nod.Contains(NodKey))
                {
                    xrec = (Xrecord)tr.GetObject(nod.GetAt(NodKey), OpenMode.ForWrite);
                }
                else
                {
                    xrec = new Xrecord();
                    nod.SetAt(NodKey, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }

                string layersRaw = string.Join(LayerDelimiter, settings.LanduseHatchLayers);

                xrec.Data = new ResultBuffer(
                    new TypedValue((int)DxfCode.Text, layersRaw),
                    new TypedValue((int)DxfCode.Text, settings.AreaUnit.ToString()),
                    new TypedValue((int)DxfCode.Text, string.IsNullOrWhiteSpace(settings.StormDistribution) ? "ATLAS14_ALTERNATING_BLOCK" : settings.StormDistribution),
                    new TypedValue((int)DxfCode.Int16, settings.HydrographTimeStepMinutes > 0 ? settings.HydrographTimeStepMinutes : 2)
                );

                tr.Commit();
            }
            catch
            {
                // Best-effort
            }
        }
    }
}
