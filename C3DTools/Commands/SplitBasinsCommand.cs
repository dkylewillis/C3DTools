using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using C3DTools.Models;
using C3DTools.Services;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace C3DTools.Commands
{
    public class SplitBasinsCommand
    {
        private const string AppNameBasin = "C3DTools_Basin";

        [CommandMethod("SPLITBASINS")]
        public void SplitBasins()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor ed    = doc.Editor;

            // ── 1. Load masks from the drawing NOD ────────────────────────────────
            var maskService  = new MaskService();
            List<MaskDefinition> allMasks    = maskService.GetMasks(db);
            List<MaskDefinition> activeMasks = allMasks.FindAll(m => m.GenerateGeometry);

            if (activeMasks.Count == 0)
            {
                ed.WriteMessage("\nNo masks with 'Generate Geometry' enabled. " +
                                "Add masks in the Basin palette → Masks tab and enable Generate Geometry.");
                return;
            }

            // ── 2. Collect all tagged, closed basin polylines ─────────────────────
            var taggedBasins = new List<(ObjectId id, string basinId, string development)>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt       = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
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
                        string basinId     = values[1].Value?.ToString() ?? string.Empty;
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

            ed.WriteMessage($"\nFound {taggedBasins.Count} tagged basin(s), {activeMasks.Count} active mask(s).");

            // ── 3. All DB modifications in one transaction ────────────────────────
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt               = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // ── 3a. Register RegApp name ──────────────────────────────────────
                RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(AppNameBasin))
                {
                    rat.UpgradeOpen();
                    RegAppTableRecord ratr = new RegAppTableRecord { Name = AppNameBasin };
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }

                int totalCreated = 0;
                int totalErased  = 0;

                foreach (MaskDefinition mask in activeMasks)
                {
                    // ── 3b. Resolve mask polyline handle → ObjectId ───────────────
                    ObjectId maskOid = maskService.ResolvePolyline(db, mask.PolylineHandle);
                    if (maskOid.IsNull || maskOid.IsErased)
                    {
                        ed.WriteMessage($"\nMask '{mask.Name}': polyline not found or was erased (handle {mask.PolylineHandle}). Skipping.");
                        continue;
                    }

                    Polyline maskPline = (Polyline)tr.GetObject(maskOid, OpenMode.ForRead);
                    if (!maskPline.Closed)
                    {
                        ed.WriteMessage($"\nMask '{mask.Name}': polyline is not closed. Skipping.");
                        continue;
                    }

                    Geometry? maskGeom = GeometryConverter.PolylineToNts(maskPline);
                    if (maskGeom == null)
                    {
                        ed.WriteMessage($"\nMask '{mask.Name}': could not convert polyline to geometry. Skipping.");
                        continue;
                    }

                    // ── 3c. Ensure layers exist for this mask ─────────────────────
                    EnsureLayer(mask.InsideLayerName,  3, tr, db);   // green
                    EnsureLayer(mask.OutsideLayerName, 1, tr, db);   // red

                    // ── 3d. Erase existing split polylines for this mask ──────────
                    var toErase = new List<ObjectId>();
                    foreach (ObjectId oid in modelSpace)
                    {
                        if (!oid.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                            continue;
                        Polyline pline = (Polyline)tr.GetObject(oid, OpenMode.ForRead);
                        if (pline.Layer == mask.InsideLayerName || pline.Layer == mask.OutsideLayerName)
                        {
                            ResultBuffer? rb = pline.GetXDataForApplication(AppNameBasin);
                            if (rb != null)
                            {
                                rb.Dispose();
                                toErase.Add(oid);
                            }
                        }
                    }

                    foreach (ObjectId oid in toErase)
                        ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();

                    totalErased += toErase.Count;

                    // ── 3e. Process each tagged basin against this mask ───────────
                    int insideCount  = 0;
                    int outsideCount = 0;

                    foreach ((ObjectId basinOid, string basinId, string development) in taggedBasins)
                    {
                        Polyline basinPline = (Polyline)tr.GetObject(basinOid, OpenMode.ForRead);
                        Geometry? basinGeom = GeometryConverter.PolylineToNts(basinPline);

                        if (basinGeom == null)
                        {
                            ed.WriteMessage($"\nSkipping basin '{basinId}': could not convert to geometry.");
                            continue;
                        }

                        // Inside = intersection with mask
                        Geometry inside = BooleanOperationHelper.Intersect(basinGeom, maskGeom);
                        if (!inside.IsEmpty)
                            insideCount += CreateSplitPolylines(inside, basinId, mask.InsideLabel,
                                mask.InsideLayerName, development, modelSpace, tr, db);

                        // Outside = difference from mask
                        Geometry outside = BooleanOperationHelper.Difference(basinGeom, maskGeom);
                        if (!outside.IsEmpty)
                            outsideCount += CreateSplitPolylines(outside, basinId, mask.OutsideLabel,
                                mask.OutsideLayerName, development, modelSpace, tr, db);
                    }

                    totalCreated += insideCount + outsideCount;
                    ed.WriteMessage($"\nMask '{mask.Name}': {insideCount} inside ({mask.InsideLayerName}), " +
                                    $"{outsideCount} outside ({mask.OutsideLayerName}).");
                }

                tr.Commit();

                if (totalErased > 0)
                    ed.WriteMessage($"\nErased {totalErased} existing split polyline(s).");

                ed.WriteMessage($"\nSPLITBASINS complete: {totalCreated} total polyline(s) created across {activeMasks.Count} mask(s).");
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
