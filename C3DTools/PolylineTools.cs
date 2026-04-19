using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using System.Collections.Generic;
using System.Linq;

namespace C3DTools
{
    public class PolylineTools
    {
        [CommandMethod("FIXGEOM")]
        public void FixGeometry()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt user to select polylines
            var opts = new PromptSelectionOptions();
            opts.MessageForAdding = "\nSelect polylines to fix: ";

            // Filter for lightweight and 2D polylines
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });

            var result = ed.GetSelection(opts, filter);
            if (result.Status != PromptStatus.OK) return;

            int fixedCount = 0;
            int skippedCount = 0;
            int multiCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                foreach (SelectedObject selObj in result.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    // Convert AutoCAD polyline to NTS geometry
                    var ntsGeom = PolylineToNts(pline);
                    if (ntsGeom == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Check if it actually needs fixing
                    var validator = new IsValidOp(ntsGeom);
                    if (validator.IsValid)
                    {
                        skippedCount++;
                        continue;
                    }

                    ed.WriteMessage($"\n  Issue: {validator.ValidationError.Message}");

                    // Fix it using library default (keepMulti = false)
                    var fixedGeometry = NetTopologySuite.Geometries.Utilities
                        .GeometryFixer.Fix(ntsGeom);

                    // Replace the original polyline with fixed geometry
                    if (fixedGeometry != null && !fixedGeometry.IsEmpty)
                    {
                        int geomCount = ReplaceWithFixedGeometry(pline, fixedGeometry, modelSpace, tr);
                        fixedCount++;
                        if (geomCount > 1)
                        {
                            multiCount++;
                            ed.WriteMessage($"\n  Split into {geomCount} geometries");
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nFIXGEOM complete: {fixedCount} fixed, {skippedCount} skipped.");
            if (multiCount > 0)
                ed.WriteMessage($"\n  {multiCount} geometries split into multiple polylines.");
        }

        [CommandMethod("PLUNION")]
        public void UnionPolylines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt for delete option
            var deleteOpts = new PromptKeywordOptions("\nDelete original polylines?");
            deleteOpts.Keywords.Add("Yes");
            deleteOpts.Keywords.Add("No");
            deleteOpts.Keywords.Default = "Yes";
            deleteOpts.AllowNone = true;

            var deleteResult = ed.GetKeywords(deleteOpts);
            if (deleteResult.Status != PromptStatus.OK && deleteResult.Status != PromptStatus.None)
                return;

            bool deleteOriginals = deleteResult.Status == PromptStatus.None || deleteResult.StringResult == "Yes";

            var filter = GetPolylineFilter();
            var result = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect polylines to union: " }, filter);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // Convert all selected polylines to NTS geometries
                var geometries = new List<Geometry>();
                var originals = new List<Polyline>();

                foreach (SelectedObject selObj in result.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    var ntsGeom = PolylineToNts(pline);
                    if (ntsGeom != null)
                    {
                        geometries.Add(ntsGeom);
                        originals.Add(pline);
                    }
                }

                if (geometries.Count < 2)
                {
                    ed.WriteMessage("\nNeed at least 2 valid polylines for union.");
                    return;
                }

                // Validate geometries before operation
                if (!ValidateAndFixGeometries(geometries, originals, ed, modelSpace, tr))
                {
                    return;
                }

                // Perform union
                Geometry unionResult = geometries[0];
                for (int i = 1; i < geometries.Count; i++)
                {
                    unionResult = unionResult.Union(geometries[i]);
                }

                // Create new polyline(s) from result
                int count = CreatePolylines(unionResult, originals[0], modelSpace, tr);

                // Delete originals if requested
                if (deleteOriginals)
                {
                    foreach (var pline in originals)
                    {
                        pline.UpgradeOpen();
                        pline.Erase();
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nPLUNION complete: {geometries.Count} polylines → {count} result(s).");
            }
        }

        [CommandMethod("PLDIFF")]
        public void DifferencePolylines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt for delete option
            var deleteOpts = new PromptKeywordOptions("\nDelete original polylines?");
            deleteOpts.Keywords.Add("Yes");
            deleteOpts.Keywords.Add("No");
            deleteOpts.Keywords.Default = "Yes";
            deleteOpts.AllowNone = true;

            var deleteResult = ed.GetKeywords(deleteOpts);
            if (deleteResult.Status != PromptStatus.OK && deleteResult.Status != PromptStatus.None)
                return;

            bool deleteOriginals = deleteResult.Status == PromptStatus.None || deleteResult.StringResult == "Yes";

            var filter = GetPolylineFilter();

            // Select base polyline
            var baseResult = ed.GetEntity(new PromptEntityOptions("\nSelect base polyline: ") { AllowNone = false });
            if (baseResult.Status != PromptStatus.OK) return;

            // Select polylines to subtract
            var subtractResult = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect polylines to subtract: " }, filter);
            if (subtractResult.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // Get base geometry
                var basePline = tr.GetObject(baseResult.ObjectId, OpenMode.ForRead) as Polyline;
                if (basePline == null)
                {
                    ed.WriteMessage("\nBase entity is not a polyline.");
                    return;
                }

                var baseGeom = PolylineToNts(basePline);
                if (baseGeom == null)
                {
                    ed.WriteMessage("\nInvalid base polyline.");
                    return;
                }

                // Collect all geometries and polylines for validation
                var allGeometries = new List<Geometry> { baseGeom };
                var allPolylines = new List<Polyline> { basePline };

                // Subtract each selected polyline
                var toSubtract = new List<Polyline>();
                foreach (SelectedObject selObj in subtractResult.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null || pline.ObjectId == basePline.ObjectId) continue;

                    var ntsGeom = PolylineToNts(pline);
                    if (ntsGeom != null)
                    {
                        allGeometries.Add(ntsGeom);
                        allPolylines.Add(pline);
                        toSubtract.Add(pline);
                    }
                }

                // Validate geometries before operation
                if (!ValidateAndFixGeometries(allGeometries, allPolylines, ed, modelSpace, tr))
                {
                    return;
                }

                // Update baseGeom after potential fixes
                baseGeom = allGeometries[0];

                // Perform difference operations
                for (int i = 1; i < allGeometries.Count; i++)
                {
                    baseGeom = baseGeom.Difference(allGeometries[i]);
                }

                if (baseGeom.IsEmpty)
                {
                    ed.WriteMessage("\nResult is empty - complete subtraction.");
                    if (deleteOriginals)
                    {
                        basePline.UpgradeOpen();
                        basePline.Erase();
                    }
                }
                else
                {
                    // Create result polyline(s)
                    int count = CreatePolylines(baseGeom, basePline, modelSpace, tr);
                    ed.WriteMessage($"\nPLDIFF complete: {count} result(s).");

                    // Delete base if requested
                    if (deleteOriginals)
                    {
                        basePline.UpgradeOpen();
                        basePline.Erase();
                    }
                }

                tr.Commit();
            }
        }

        [CommandMethod("PLINT")]
        public void IntersectPolylines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var filter = GetPolylineFilter();
            var result = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect polylines to intersect: " }, filter);
            if (result.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                // Convert all selected polylines to NTS geometries
                var geometries = new List<Geometry>();
                var originals = new List<Polyline>();

                foreach (SelectedObject selObj in result.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    var ntsGeom = PolylineToNts(pline);
                    if (ntsGeom != null)
                    {
                        geometries.Add(ntsGeom);
                        originals.Add(pline);
                    }
                }

                if (geometries.Count < 2)
                {
                    ed.WriteMessage("\nNeed at least 2 valid polylines for intersection.");
                    return;
                }

                // Validate geometries before operation
                if (!ValidateAndFixGeometries(geometries, originals, ed, modelSpace, tr))
                {
                    return;
                }

                // Perform intersection
                Geometry intersectResult = geometries[0];
                for (int i = 1; i < geometries.Count; i++)
                {
                    intersectResult = intersectResult.Intersection(geometries[i]);
                    if (intersectResult.IsEmpty)
                    {
                        ed.WriteMessage("\nNo intersection found.");
                        return;
                    }
                }

                // Create new polyline(s) from result
                int count = CreatePolylines(intersectResult, originals[0], modelSpace, tr);

                tr.Commit();
                ed.WriteMessage($"\nPLINT complete: {count} result(s).");
            }
        }

        private SelectionFilter GetPolylineFilter()
        {
            return new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });
        }

        private int CreatePolylines(Geometry geom, Polyline template, BlockTableRecord modelSpace, Transaction tr)
        {
            var geometries = new List<Geometry>();

            // Extract individual geometries from collections
            if (geom is GeometryCollection collection)
            {
                for (int i = 0; i < collection.NumGeometries; i++)
                    geometries.Add(collection.GetGeometryN(i));
            }
            else
            {
                geometries.Add(geom);
            }

            // Filter to only Polygon and LineString types
            geometries = geometries.Where(g => g is Polygon || g is LineString).ToList();
            if (geometries.Count == 0) return 0;

            // Create new polylines for all geometries
            foreach (var geometry in geometries)
            {
                var newPline = new Polyline();

                // Copy properties from template
                newPline.Layer = template.Layer;
                newPline.Color = template.Color;
                newPline.LineWeight = template.LineWeight;
                newPline.Linetype = template.Linetype;

                // Set geometry
                SetPolylineGeometry(newPline, geometry);

                // Add to model space
                modelSpace.AppendEntity(newPline);
                tr.AddNewlyCreatedDBObject(newPline, true);
            }

            return geometries.Count;
        }

        private Geometry PolylineToNts(Polyline pline)
        {
            var coords = new List<Coordinate>();

            for (int i = 0; i < pline.NumberOfVertices; i++)
            {
                // NOTE: this flattens bulges (arcs) to straight segments.
                // For arc-accurate conversion, tessellate bulge segments.
                var pt = pline.GetPoint2dAt(i);
                coords.Add(new Coordinate(pt.X, pt.Y));
            }

            if (coords.Count < 2) return null;

            var gf = new GeometryFactory();

            if (pline.Closed)
            {
                // Close the ring if needed
                if (!coords[0].Equals2D(coords[coords.Count - 1]))
                    coords.Add(coords[0].Copy());

                if (coords.Count < 4) return null; // minimum for a valid ring
                return gf.CreatePolygon(coords.ToArray());
            }
            else
            {
                return gf.CreateLineString(coords.ToArray());
            }
        }

        private int ReplaceWithFixedGeometry(Polyline original, Geometry fixedGeom, BlockTableRecord modelSpace, Transaction tr)
        {
            var geometries = new List<Geometry>();

            // Extract individual geometries from collections
            if (fixedGeom is GeometryCollection collection)
            {
                for (int i = 0; i < collection.NumGeometries; i++)
                    geometries.Add(collection.GetGeometryN(i));
            }
            else
            {
                geometries.Add(fixedGeom);
            }

            // Filter to only Polygon and LineString types
            geometries = geometries.Where(g => g is Polygon || g is LineString).ToList();
            if (geometries.Count == 0) return 0;

            // Create new polylines for all geometries (including replacement for original)
            for (int i = 0; i < geometries.Count; i++)
            {
                var newPline = new Polyline();

                // Copy properties from original
                newPline.Layer = original.Layer;
                newPline.Color = original.Color;
                newPline.LineWeight = original.LineWeight;
                newPline.Linetype = original.Linetype;

                // Set geometry
                SetPolylineGeometry(newPline, geometries[i]);

                // Add to model space
                modelSpace.AppendEntity(newPline);
                tr.AddNewlyCreatedDBObject(newPline, true);
            }

            // Delete the original polyline
            original.UpgradeOpen();
            original.Erase();

            return geometries.Count;
        }

        private void SetPolylineGeometry(Polyline pline, Geometry geom)
        {
            // Extract coordinates from the geometry
            Coordinate[] coords;

            if (geom is Polygon poly)
                coords = poly.ExteriorRing.Coordinates;
            else if (geom is LineString line)
                coords = line.Coordinates;
            else
                return;

            // Add vertices
            bool isClosed = geom is Polygon;
            int count = isClosed ? coords.Length - 1 : coords.Length; // skip closing duplicate

            for (int i = 0; i < count; i++)
            {
                pline.AddVertexAt(i,
                    new Point2d(coords[i].X, coords[i].Y),
                    0, 0, 0); // bulge=0, no arc
            }

            pline.Closed = isClosed;
        }

        private bool ValidateAndFixGeometries(List<Geometry> geometries, List<Polyline> polylines, Editor ed, BlockTableRecord modelSpace, Transaction tr)
        {
            var invalidIndices = new List<int>();
            var invalidMessages = new List<string>();

            // Check each geometry for validity
            for (int i = 0; i < geometries.Count; i++)
            {
                var validator = new IsValidOp(geometries[i]);
                if (!validator.IsValid)
                {
                    invalidIndices.Add(i);
                    invalidMessages.Add(validator.ValidationError.Message);
                }
            }

            // If all geometries are valid, proceed
            if (invalidIndices.Count == 0)
                return true;

            // Report invalid geometries
            ed.WriteMessage($"\n{invalidIndices.Count} invalid geometr{(invalidIndices.Count == 1 ? "y" : "ies")} detected:");
            for (int i = 0; i < invalidIndices.Count; i++)
            {
                ed.WriteMessage($"\n  Polyline {invalidIndices[i] + 1}: {invalidMessages[i]}");
            }

            // Prompt user for action
            var fixOpts = new PromptKeywordOptions("\nFix invalid geometries automatically?");
            fixOpts.Keywords.Add("Yes");
            fixOpts.Keywords.Add("No");
            fixOpts.Keywords.Add("Cancel");
            fixOpts.Keywords.Default = "Yes";
            fixOpts.AllowNone = true;

            var fixResult = ed.GetKeywords(fixOpts);
            if (fixResult.Status != PromptStatus.OK && fixResult.Status != PromptStatus.None)
                return false;

            string choice = fixResult.Status == PromptStatus.None ? "Yes" : fixResult.StringResult;

            if (choice == "Cancel")
            {
                ed.WriteMessage("\nOperation cancelled.");
                return false;
            }

            if (choice == "No")
            {
                ed.WriteMessage("\nProceeding with invalid geometries (may produce unexpected results).");
                return true;
            }

            // Fix invalid geometries
            ed.WriteMessage("\nFixing geometries...");
            int fixedCount = 0;

            foreach (int idx in invalidIndices)
            {
                var fixedGeom = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(geometries[idx]);
                if (fixedGeom != null && !fixedGeom.IsEmpty)
                {
                    geometries[idx] = fixedGeom;
                    fixedCount++;
                }
            }

            ed.WriteMessage($"\n{fixedCount} geometr{(fixedCount == 1 ? "y" : "ies")} fixed. Proceeding with operation.");
            return true;
        }
    }
}