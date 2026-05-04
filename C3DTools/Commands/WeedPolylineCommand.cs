using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using NetTopologySuite.Simplify;
using C3DTools.Helpers;

namespace C3DTools.Commands
{
    public class WeedPolylineCommand
    {
        [CommandMethod("WEEDPLINE")]
        public void WeedPolyline()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt for tolerance
            var distOpts = new PromptDistanceOptions("\nEnter weed tolerance: ")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = 0.1,
                UseDefaultValue = true
            };

            var distResult = ed.GetDistance(distOpts);
            if (distResult.Status != PromptStatus.OK) return;

            double tolerance = distResult.Value;

            // Prompt for polyline selection
            var selOpts = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect polylines to weed: "
            };

            var filter = PolylineHelper.GetPolylineFilter();
            var selResult = ed.GetSelection(selOpts, filter);
            if (selResult.Status != PromptStatus.OK) return;

            int weededCount = 0;
            int skippedCount = 0;
            int totalRemoved = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selResult.Value)
                {
                    var pline = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                    if (pline == null) continue;

                    int originalCount = pline.NumberOfVertices;

                    var ntsGeom = GeometryConverter.PolylineToNts(pline);
                    if (ntsGeom == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // VWLineSimplifier operates on Coordinate arrays
                    var simplifiedCoords = VWLineSimplifier.Simplify(ntsGeom.Coordinates, tolerance);
                    if (simplifiedCoords == null || simplifiedCoords.Length < 2)
                    {
                        skippedCount++;
                        continue;
                    }

                    // For closed polylines NTS includes a closing duplicate — subtract 1
                    int simplifiedCount = pline.Closed ? simplifiedCoords.Length - 1 : simplifiedCoords.Length;

                    if (simplifiedCount >= originalCount)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Derive the coordinate list the same way NtsToPolyline does
                    var outCoords = pline.Closed
                        ? simplifiedCoords.AsSpan(0, simplifiedCount).ToArray()
                        : simplifiedCoords;

                    pline.UpgradeOpen();

                    // Zero out bulge on every existing vertex first
                    for (int i = 0; i < originalCount; i++)
                        pline.SetBulgeAt(i, 0);

                    // Overwrite vertices that already exist
                    int overwriteCount = Math.Min(simplifiedCount, originalCount);
                    for (int i = 0; i < overwriteCount; i++)
                        pline.SetPointAt(i, new Autodesk.AutoCAD.Geometry.Point2d(outCoords[i].X, outCoords[i].Y));

                    // Add new vertices if the simplified result is somehow longer (shouldn't happen, but safe)
                    for (int i = originalCount; i < simplifiedCount; i++)
                        pline.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(outCoords[i].X, outCoords[i].Y), 0, 0, 0);

                    // Remove excess vertices from the end (safe direction — never drops below minimum)
                    for (int i = pline.NumberOfVertices - 1; i >= simplifiedCount; i--)
                        pline.RemoveVertexAt(i);

                    int removed = originalCount - pline.NumberOfVertices;
                    totalRemoved += removed;
                    weededCount++;
                    ed.WriteMessage($"\n  Handle {pline.Handle}: {originalCount} → {pline.NumberOfVertices} vertices ({removed} removed)");
                }

                tr.Commit();
            }

            ed.WriteMessage(
                $"\nWEEDPLINE complete: {weededCount} weeded ({totalRemoved} vertices removed), {skippedCount} skipped.");
        }
    }
}
