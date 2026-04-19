using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using C3DTools.Helpers;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Valid;
using System;
using System.Collections.Generic;

namespace C3DTools.Commands
{
    public class FixHatchCommand
    {
        [CommandMethod("FIXHATCH")]
        public void FixHatch()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            // -- 1. Select hatches ------------------------------------------------
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect hatches to fix: ";

            SelectionFilter filter = new SelectionFilter(new TypedValue[]
            {
                new TypedValue((int)DxfCode.Start, "HATCH")
            });

            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo hatches selected.");
                return;
            }

            int fixedCount   = 0;
            int skippedCount = 0;
            int errorCount   = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

                foreach (SelectedObject selObj in psr.Value)
                {
                    Hatch original = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Hatch;
                    if (original == null) continue;

                    Handle handle = original.Handle;

                    // -- 2. Extract geometry from hatch ---------------------------
                    Geometry ntsGeom;
                    try
                    {
                        ntsGeom = GeometryConverter.HatchToNts(original);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\nHatch {handle}: conversion error — {ex.Message}");
                        errorCount++;
                        continue;
                    }

                    if (ntsGeom == null || ntsGeom.IsEmpty)
                    {
                        ed.WriteMessage($"\nHatch {handle}: could not extract geometry, skipping.");
                        errorCount++;
                        continue;
                    }

                    // -- 3. Validate ----------------------------------------------
                    IsValidOp validator = new IsValidOp(ntsGeom);
                    if (validator.IsValid)
                    {
                        skippedCount++;
                        continue;
                    }

                    ed.WriteMessage($"\nHatch {handle}: {validator.ValidationError.Message}");

                    // -- 4. Fix geometry ------------------------------------------
                    Geometry fixedGeom;
                    try
                    {
                        fixedGeom = GeometryFixer.Fix(ntsGeom);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n  Fix error — {ex.Message}");
                        errorCount++;
                        continue;
                    }

                    // GeometryFixer cannot resolve "Hole lies outside shell" —
                    // fall back to dropping every hole that is not inside its shell.
                    if ((fixedGeom == null || fixedGeom.IsEmpty) &&
                        validator.ValidationError.ErrorType ==
                            NetTopologySuite.Operation.Valid.TopologyValidationErrors.HoleOutsideShell)
                    {
                        fixedGeom = FixHolesOutsideShell(ntsGeom);
                        if (fixedGeom != null && !fixedGeom.IsEmpty)
                            ed.WriteMessage($"\n  Applied fallback: re-classified mis-wound rings.");
                    }

                    if (fixedGeom == null || fixedGeom.IsEmpty)
                    {
                        ed.WriteMessage($"\n  Unable to fix geometry.");
                        errorCount++;
                        continue;
                    }

                    // -- 5. Collect individual polygons to recreate ---------------
                    List<Polygon> polygons = FlattenPolygons(fixedGeom);
                    if (polygons.Count == 0)
                    {
                        ed.WriteMessage($"\n  Fixed geometry has no usable polygons.");
                        errorCount++;
                        continue;
                    }

                    // Capture hatch properties before touching the original
                    string           layer       = original.Layer;
                    LineWeight       lineWeight  = original.LineWeight;
                    string           patternName = original.PatternName;
                    HatchPatternType patternType = original.PatternType;
                    double           patScale    = original.PatternScale;
                    double           patAngle    = original.PatternAngle;

                    // -- 6. Build replacement hatch(es) ---------------------------
                    int createdThisHatch = 0;
                    bool anyFailed       = false;

                    foreach (Polygon poly in polygons)
                    {
                        // -- 6a. Create temp boundary polylines ---------------------
                        // Outer ring
                        Polyline outerPline = RingToPolyline(poly.ExteriorRing, layer, lineWeight);
                        if (outerPline == null) { anyFailed = true; continue; }
                        btr.AppendEntity(outerPline);
                        tr.AddNewlyCreatedDBObject(outerPline, true);

                        // Hole rings
                        List<Polyline> holePlines = new List<Polyline>();
                        for (int h = 0; h < poly.NumInteriorRings; h++)
                        {
                            Polyline holePline = RingToPolyline(poly.GetInteriorRingN(h), layer, lineWeight);
                            if (holePline == null) continue;
                            btr.AppendEntity(holePline);
                            tr.AddNewlyCreatedDBObject(holePline, true);
                            holePlines.Add(holePline);
                        }

                        // -- 6b. Create hatch from boundary ObjectIds ---------------
                        Hatch newHatch = new Hatch();
                        btr.AppendEntity(newHatch);
                        tr.AddNewlyCreatedDBObject(newHatch, true);

                        bool hatchOk = false;
                        try
                        {
                            newHatch.SetHatchPattern(patternType, patternName);
                            newHatch.Layer      = layer;
                            newHatch.Associative = false; // non-associative so we can erase temp boundaries

                            // Outer loop
                            ObjectIdCollection outerIds = new ObjectIdCollection();
                            outerIds.Add(outerPline.ObjectId);
                            newHatch.AppendLoop(HatchLoopTypes.Outermost, outerIds);

                            // Hole loops
                            foreach (Polyline holePline in holePlines)
                            {
                                ObjectIdCollection holeIds = new ObjectIdCollection();
                                holeIds.Add(holePline.ObjectId);
                                newHatch.AppendLoop(HatchLoopTypes.Default, holeIds);
                            }

                            newHatch.PatternScale = patScale;
                            newHatch.PatternAngle = patAngle;
                            newHatch.EvaluateHatch(true);
                            hatchOk = true;
                            createdThisHatch++;
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n  Failed to create hatch polygon — {ex.Message}");
                            newHatch.Erase();
                            anyFailed = true;
                        }

                        // -- 6c. Erase temp boundary polylines ----------------------
                        // Non-associative hatch stores loop data internally after EvaluateHatch,
                        // so the boundary polylines are no longer needed.
                        outerPline.UpgradeOpen();
                        outerPline.Erase();
                        foreach (Polyline hp in holePlines)
                        {
                            hp.UpgradeOpen();
                            hp.Erase();
                        }

                        if (hatchOk)
                        {
                            string areaStr;
                            try   { areaStr = $"{newHatch.Area:F4}"; }
                            catch { areaStr = "n/a"; }
                            ed.WriteMessage($"\n  Created replacement hatch (area: {areaStr})");
                        }
                    }

                    // -- 7. Erase original hatch only if at least one replacement was made --
                    if (createdThisHatch > 0)
                    {
                        original.UpgradeOpen();
                        original.Erase();
                        fixedCount++;
                        ed.WriteMessage($"\n  Replaced with {createdThisHatch} hatch(es).");
                    }
                    else
                    {
                        ed.WriteMessage($"\n  Could not replace hatch {handle} — original kept.");
                        errorCount++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage(
                $"\n\nFIXHATCH complete: {fixedCount} fixed, {skippedCount} already valid, {errorCount} error(s).");
        }

        // -- Helpers --------------------------------------------------------------

        /// <summary>
        /// Recursively collects all Polygon instances from any Geometry type.
        /// </summary>
        private static List<Polygon> FlattenPolygons(Geometry geom)
        {
            var result = new List<Polygon>();
            if (geom is Polygon p)
            {
                if (!p.IsEmpty && p.Area > 1e-10) result.Add(p);
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    result.AddRange(FlattenPolygons(gc.GetGeometryN(i)));
            }
            return result;
        }

        /// <summary>
        /// Creates a closed Polyline from an NTS LinearRing.
        /// Returns null if the ring has fewer than 3 unique vertices.
        /// </summary>
        private static Polyline RingToPolyline(LineString ring, string layer, LineWeight lineWeight)
        {
            Coordinate[] coords = ring.Coordinates;
            int vertCount = coords.Length - 1; // NTS closes the ring with a duplicate last point
            if (vertCount < 3) return null;

            Polyline pline = new Polyline();
            pline.Layer      = layer;
            pline.LineWeight = lineWeight;

            for (int i = 0; i < vertCount; i++)
                pline.AddVertexAt(i, new Point2d(coords[i].X, coords[i].Y), 0, 0, 0);

            pline.Closed = true;
            return pline;
        }

        /// <summary>
        /// Fixes "Hole lies outside shell" by re-classifying each ring by whether it truly
        /// sits inside an identified shell.
        ///
        /// A "hole" that is NOT inside any shell is almost always an outer boundary that was
        /// wound clockwise (AutoCAD does not enforce winding direction), so it was mis-classified
        /// during HatchToNts conversion.  Promoting it to its own shell recovers the geometry
        /// instead of silently erasing it.
        ///
        /// Handles Polygon and MultiPolygon inputs and returns a Polygon or MultiPolygon.
        /// </summary>
        private static Geometry FixHolesOutsideShell(Geometry geom)
        {
            GeometryFactory gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();

            // Collect all rings (shells + holes) from the input
            var allRings = new List<LinearRing>();

            void CollectRings(Polygon poly)
            {
                if (poly.ExteriorRing is LinearRing shell) allRings.Add(shell);
                for (int i = 0; i < poly.NumInteriorRings; i++)
                    if (poly.GetInteriorRingN(i) is LinearRing hole) allRings.Add(hole);
            }

            if (geom is Polygon sp) CollectRings(sp);
            else if (geom is MultiPolygon mp)
                for (int i = 0; i < mp.NumGeometries; i++)
                    if (mp.GetGeometryN(i) is Polygon part) CollectRings(part);

            if (allRings.Count == 0) return null;

            // Build a candidate shell polygon for every ring — winding is irrelevant here
            var candidates = new List<Polygon>();
            foreach (LinearRing ring in allRings)
            {
                try
                {
                    // Normalize: create a polygon and let NTS sort the winding
                    Polygon candidate = gf.CreatePolygon(ring);
                    if (!candidate.IsEmpty && candidate.Area > 1e-10)
                        candidates.Add(candidate);
                }
                catch { }
            }

            if (candidates.Count == 0) return null;

            // Sort largest-first: the biggest ring is always the outermost shell
            candidates.Sort((a, b) => b.Area.CompareTo(a.Area));

            // Assign each smaller ring as either a hole in the ring that contains it,
            // or promote it to its own shell if no containing shell exists.
            var shells     = new List<Polygon>  { candidates[0] };
            var shellHoles = new List<List<LinearRing>> { new List<LinearRing>() };

            for (int i = 1; i < candidates.Count; i++)
            {
                Polygon candidate  = candidates[i];
                bool    assignedHole = false;

                for (int s = 0; s < shells.Count; s++)
                {
                    try
                    {
                        if (shells[s].Contains(candidate.Centroid))
                        {
                            shellHoles[s].Add(candidate.ExteriorRing as LinearRing);
                            assignedHole = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!assignedHole)
                {
                    // Not inside any known shell ? it IS a shell (just mis-wound)
                    shells.Add(candidate);
                    shellHoles.Add(new List<LinearRing>());
                }
            }

            // Rebuild clean polygons
            var result = new List<Polygon>();
            for (int s = 0; s < shells.Count; s++)
            {
                try
                {
                    LinearRing shellRing = shells[s].ExteriorRing as LinearRing;
                    result.Add(gf.CreatePolygon(shellRing, shellHoles[s].ToArray()));
                }
                catch
                {
                    result.Add(shells[s]); // fall back to hole-free shell
                }
            }

            if (result.Count == 0) return null;
            if (result.Count == 1) return result[0];
            return gf.CreateMultiPolygon(result.ToArray());
        }
    }
}
