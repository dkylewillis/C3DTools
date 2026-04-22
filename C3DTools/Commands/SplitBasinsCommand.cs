using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using C3DTools.Services;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace C3DTools.Commands
{
    public class SplitBasinsCommand
    {
        private const string AppNameBasin = "C3DTools_Basin";
        private const string OnsiteLabel = "ONSITE";
        private const string OffsiteLabel = "OFFSITE";

        [CommandMethod("SPLITBASINS")]
        public void SplitBasins()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Load settings (drawing-level overrides global defaults)
            var settings = new SettingsResolver().Resolve(db);
            string onsiteLayer = settings.OnsiteLayer;
            string offsiteLayer = settings.OffsiteLayer;

            // ── 1. Collect all C3DTools_Basin-tagged closed polylines ───────────────
            var taggedBasins = new List<(ObjectId id, string basinId, string development)>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId oid in ms)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;

                    Polyline pline = (Polyline)tr.GetObject(oid, OpenMode.ForRead);
                    if (!pline.Closed) continue;

                    ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                    if (rb == null) continue;

                    TypedValue[] values = rb.AsArray();
                    rb.Dispose();

                    if (values.Length > 1 && values[1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    {
                        string basinId = values[1].Value?.ToString() ?? string.Empty;
                        string development = (values.Length > 3 && values[3].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                            ? values[3].Value?.ToString() ?? string.Empty
                            : string.Empty;
                        taggedBasins.Add((oid, basinId, development));
                    }
                }

                tr.Commit();
            }

            if (taggedBasins.Count == 0)
            {
                ed.WriteMessage("\nNo polylines tagged with C3DTools_Basin found in the drawing.");
                return;
            }

            ed.WriteMessage($"\nFound {taggedBasins.Count} tagged basin(s).");

            // ── 2. Prompt for site boundary polyline ──────────────────────────────
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect site boundary polyline: ");
            peo.SetRejectMessage("\nMust be a closed polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline siteBoundary = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                if (!siteBoundary.Closed)
                {
                    ed.WriteMessage("\nSelected polyline is not closed. Command cancelled.");
                    tr.Commit();
                    return;
                }
                tr.Commit();
            }

            // ── 3. All DB modifications in one transaction ────────────────────────
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // ── 3a. Register RegApp name ──────────────────────────────────
                RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

                if (!rat.Has(AppNameBasin))
                {
                    rat.UpgradeOpen();
                    RegAppTableRecord ratr = new RegAppTableRecord { Name = AppNameBasin };
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }

                // ── 3b. Ensure layers exist ────────────────────────────────────
                EnsureLayer(onsiteLayer, 3, tr, db);   // 3 = green
                EnsureLayer(offsiteLayer, 1, tr, db);  // 1 = red

                // ── 3c. Erase existing split basin polylines (Boundary = ONSITE/OFFSITE) ──────────
                var toErase = new List<ObjectId>();
                foreach (ObjectId oid in modelSpace)
                {
                    if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                        continue;
                    Polyline pline = (Polyline)tr.GetObject(oid, OpenMode.ForRead);
                    ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                    if (rb != null)
                    {
                        TypedValue[] values = rb.AsArray();
                        // Check if Boundary (index 2) is ONSITE or OFFSITE
                        if (values.Length > 2 && values[2].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            string? boundary = values[2].Value?.ToString();
                            if (boundary == OnsiteLabel || boundary == OffsiteLabel)
                            {
                                toErase.Add(oid);
                            }
                        }
                        rb.Dispose();
                    }
                }

                foreach (ObjectId oid in toErase)
                {
                    DBObject obj = tr.GetObject(oid, OpenMode.ForWrite);
                    obj.Erase();
                }

                if (toErase.Count > 0)
                    ed.WriteMessage($"\nErased {toErase.Count} existing split basin polyline(s).");

                // ── 3d. Get site boundary NTS geometry ────────────────────────
                Polyline sitePline = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                Geometry? siteGeom = GeometryConverter.PolylineToNts(sitePline);

                if (siteGeom == null)
                {
                    ed.WriteMessage("\nCould not convert site boundary to geometry. Command cancelled.");
                    return;
                }

                // ── 3e. Process each tagged basin ──────────────────────────────
                int onsiteCount = 0;
                int offsiteCount = 0;
                int processedCount = 0;

                foreach ((ObjectId basinOid, string basinId, string development) in taggedBasins)
                {
                    Polyline basinPline = (Polyline)tr.GetObject(basinOid, OpenMode.ForRead);
                    Geometry? basinGeom = GeometryConverter.PolylineToNts(basinPline);

                    if (basinGeom == null)
                    {
                        ed.WriteMessage($"\nSkipping basin '{basinId}': could not convert to geometry.");
                        continue;
                    }

                    processedCount++;

                    // Intersection → onsite
                    Geometry onsiteGeom = BooleanOperationHelper.Intersect(basinGeom, siteGeom);
                    if (!onsiteGeom.IsEmpty)
                        onsiteCount += CreateSplitPolylines(onsiteGeom, basinId, OnsiteLabel, onsiteLayer, development, modelSpace, tr, db);

                    // Difference → offsite
                    Geometry offsiteGeom = BooleanOperationHelper.Difference(basinGeom, siteGeom);
                    if (!offsiteGeom.IsEmpty)
                        offsiteCount += CreateSplitPolylines(offsiteGeom, basinId, OffsiteLabel, offsiteLayer, development, modelSpace, tr, db);
                }

                tr.Commit();

                ed.WriteMessage($"\nSPLITBASINS complete: {processedCount} basin(s) processed, " +
                                $"{onsiteCount} onsite piece(s) created, {offsiteCount} offsite piece(s) created.");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates new closed polylines from an NTS geometry result, assigns them to
        /// the given layer, and stamps them with C3DTools_BasinSplit XData.
        /// Returns the number of polylines created.
        /// </summary>
        private static int CreateSplitPolylines(
            Geometry geom,
            string basinId,
            string splitLabel,
            string layer,
            string development,
            BlockTableRecord modelSpace,
            Transaction tr,
            Database db)
        {
            var polygons = GeometryHelper.FlattenPolygons(geom);
            int count = 0;

            foreach (Polygon poly in polygons)
            {
                Polyline newPline = new Polyline();
                newPline.Layer = layer;
                GeometryConverter.SetPolylineGeometry(newPline, poly);

                modelSpace.AppendEntity(newPline);
                tr.AddNewlyCreatedDBObject(newPline, true);

                // Tag with basin ID, Boundary, and Development from parent
                // [AppName, BasinId, Boundary, Development]
                string boundary = splitLabel; // "ONSITE" or "OFFSITE"
                ResultBuffer rbBasin = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameBasin),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, basinId),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, boundary),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, development)
                );
                newPline.XData = rbBasin;
                rbBasin.Dispose();

                count++;
            }

            return count;
        }

        /// <summary>
        /// Ensures a layer exists in the drawing; creates it with the given ACI color
        /// index if it does not.
        /// </summary>
        private static void EnsureLayer(string layerName, short colorIndex, Transaction tr, Database db)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
