using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;

namespace C3DTools.Helpers
{
    public static class GeometryConverter
    {
        public static Geometry? PolylineToNts(Polyline pline)
        {
            var coords = new List<Coordinate>();
            int n = pline.NumberOfVertices;

            for (int i = 0; i < n; i++)
            {
                var pt = pline.GetPoint2dAt(i);
                coords.Add(new Coordinate(pt.X, pt.Y));

                double bulge = pline.GetBulgeAt(i);
                if (Math.Abs(bulge) > 1e-9)
                {
                    // Tessellate the arc defined by this bulge to the next vertex.
                    // For a closed polyline the next vertex of the last segment wraps to index 0.
                    int nextIdx = (i + 1) % n;
                    var ptNext = pline.GetPoint2dAt(nextIdx);
                    var arcCoords = TessellateBulge(
                        new Point2d(pt.X, pt.Y),
                        new Point2d(ptNext.X, ptNext.Y),
                        bulge);
                    // arcCoords includes both endpoints; skip the first (already added above)
                    // and skip the last (will be added as the next vertex).
                    for (int k = 1; k < arcCoords.Count - 1; k++)
                        coords.Add(arcCoords[k]);
                }
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

        public static void SetPolylineGeometry(Polyline pline, Geometry geom)
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

        /// <summary>
        /// Converts a Hatch entity to an NTS Polygon or MultiPolygon.
        ///
        /// Classification strategy: area-based containment, NOT winding direction.
        /// AutoCAD does not guarantee winding direction on hatch loop coordinates,
        /// and HatchLoopTypes flags are inconsistently set by different authoring tools
        /// (Civil 3D, Dynamo, imports, etc.).
        ///
        /// Rings are sorted largest-area-first. Each ring is tested against already-
        /// accepted shells: if its centroid is contained by a shell it becomes a hole
        /// for that shell, otherwise it is promoted to a new shell.
        /// </summary>
        public static Geometry? HatchToNts(Hatch hatch)
        {
            var gf       = new GeometryFactory();
            var polygons = new List<Polygon>();
            int numLoops = hatch.NumberOfLoops;

            if (numLoops == 0) return null;

            // ── Step 1: extract raw rings (no classification yet) ─────────────────
            var allRings = new List<List<Coordinate>>();

            for (int i = 0; i < numLoops; i++)
            {
                HatchLoop? loop = null;
                try   { loop = hatch.GetLoopAt(i); }
                catch { continue; }
                if (loop == null) continue;

                List<Coordinate>? coords = null;
                try   { coords = ExtractLoopCoordinates(loop); }
                catch { continue; }

                if (coords == null || coords.Count < 3) continue;

                if (!coords[0].Equals2D(coords[coords.Count - 1]))
                    coords.Add(coords[0].Copy());

                if (coords.Count < 4) continue;

                allRings.Add(coords);
            }

            if (allRings.Count == 0) return null;

            // ── Step 2: sort largest-area-first ───────────────────────────────────
            // Use magnitude only — sign encodes winding direction which we ignore.
            allRings.Sort((a, b) =>
                Math.Abs(ComputeSignedArea(b)).CompareTo(Math.Abs(ComputeSignedArea(a))));

            // ── Step 3: assign shells vs holes by centroid containment ────────────
            var shellCoordsList = new List<List<Coordinate>>();
            var shellHolesList  = new List<List<List<Coordinate>>>();

            foreach (var ringCoords in allRings)
            {
                Coordinate centroid       = ComputeCentroid(ringCoords);
                bool       assignedAsHole = false;

                for (int s = 0; s < shellCoordsList.Count; s++)
                {
                    try
                    {
                        LinearRing tempShell = gf.CreateLinearRing(shellCoordsList[s].ToArray());
                        Polygon    tempPoly  = gf.CreatePolygon(tempShell);
                        if (tempPoly.Contains(gf.CreatePoint(centroid)))
                        {
                            shellHolesList[s].Add(ringCoords);
                            assignedAsHole = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!assignedAsHole)
                {
                    shellCoordsList.Add(ringCoords);
                    shellHolesList.Add(new List<List<Coordinate>>());
                }
            }

            if (shellCoordsList.Count == 0) return null;

            // ── Step 4: build NTS geometry ────────────────────────────────────────
            try
            {
                if (shellCoordsList.Count == 1)
                {
                    LinearRing   shell = gf.CreateLinearRing(shellCoordsList[0].ToArray());
                    LinearRing[] holes = shellHolesList[0]
                        .ConvertAll(h => gf.CreateLinearRing(h.ToArray())).ToArray();
                    return gf.CreatePolygon(shell, holes);
                }
                else
                {
                    for (int s = 0; s < shellCoordsList.Count; s++)
                    {
                        LinearRing   shell = gf.CreateLinearRing(shellCoordsList[s].ToArray());
                        LinearRing[] holes = shellHolesList[s]
                            .ConvertAll(h => gf.CreateLinearRing(h.ToArray())).ToArray();
                        polygons.Add(gf.CreatePolygon(shell, holes));
                    }
                    return gf.CreateMultiPolygon(polygons.ToArray());
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts coordinates from a HatchLoop.
        /// Handles PolylineBoundary, CircularArcBoundary, EllipticalArcBoundary, LineBoundary, SplineBoundary.
        /// </summary>
        private static List<Coordinate>? ExtractLoopCoordinates(HatchLoop loop)
        {
            List<Coordinate> coords = new List<Coordinate>();

            try
            {
                if (loop.IsPolyline)
                {
                    BulgeVertexCollection bulges = loop.Polyline;
                    if (bulges == null || bulges.Count == 0)
                        return null;

                    int bCount = bulges.Count;
                    for (int b = 0; b < bCount; b++)
                    {
                        BulgeVertex bv = bulges[b];
                        coords.Add(new Coordinate(bv.Vertex.X, bv.Vertex.Y));

                        if (Math.Abs(bv.Bulge) > 1e-9)
                        {
                            // Tessellate the arc to the next vertex (wraps for closed loop).
                            BulgeVertex bvNext = bulges[(b + 1) % bCount];
                            var arcCoords = TessellateBulge(
                                bv.Vertex, bvNext.Vertex, bv.Bulge);
                            // Skip first and last — they are the shared endpoints.
                            for (int k = 1; k < arcCoords.Count - 1; k++)
                                coords.Add(arcCoords[k]);
                        }
                    }
                }
                else
                {
                    // Loop is composed of individual curves
                    if (loop.Curves == null || loop.Curves.Count == 0)
                        return null;

                    foreach (Curve2d curve in loop.Curves)
                    {
                        try
                        {
                            if (curve is LineSegment2d line)
                            {
                                coords.Add(new Coordinate(line.StartPoint.X, line.StartPoint.Y));
                                // EndPoint will be StartPoint of next segment
                            }
                            else if (curve is CircularArc2d arc)
                            {
                                // Tessellate circular arc
                                var arcCoords = TessellateArc(arc);
                                coords.AddRange(arcCoords);
                            }
                            else if (curve is EllipticalArc2d ellipse)
                            {
                                // Tessellate elliptical arc
                                var ellipseCoords = TessellateEllipse(ellipse);
                                coords.AddRange(ellipseCoords);
                            }
                            // Unsupported curve types (spline, etc.) are silently skipped
                        }
                        catch
                        {
                            // Skip problematic curves
                            continue;
                        }
                    }

                    // Add final endpoint if missing
                    if (loop.Curves.Count > 0)
                    {
                        try
                        {
                            var lastCurve = loop.Curves[loop.Curves.Count - 1];
                            Point2d endPt = GetEndPoint(lastCurve);
                            if (coords.Count > 0)
                            {
                                var lastCoord = coords[coords.Count - 1];
                                if (Math.Abs(lastCoord.X - endPt.X) > 1e-6 || Math.Abs(lastCoord.Y - endPt.Y) > 1e-6)
                                {
                                    coords.Add(new Coordinate(endPt.X, endPt.Y));
                                }
                            }
                        }
                        catch
                        {
                            // Skip if we can't get endpoint
                        }
                    }
                }
            }
            catch
            {
                return null; // Failed to extract coordinates
            }

            return coords;
        }

        /// <summary>
        /// Tessellates an AutoCAD bulge arc into line-segment coordinates.
        /// A bulge is tan(theta/4) where theta is the included angle of the arc.
        /// Positive bulge = CCW arc, negative = CW arc.
        /// </summary>
        private static List<Coordinate> TessellateBulge(Point2d start, Point2d end, double bulge, int segments = 16)
        {
            // Derive arc centre and radius from the bulge value.
            double dx   = end.X - start.X;
            double dy   = end.Y - start.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 1e-10) return new List<Coordinate> { new Coordinate(start.X, start.Y), new Coordinate(end.X, end.Y) };

            double halfAngle = 2.0 * Math.Atan(bulge);          // half the included angle
            double radius    = chord / (2.0 * Math.Sin(halfAngle));

            // Mid-point of the chord
            double mx = (start.X + end.X) / 2.0;
            double my = (start.Y + end.Y) / 2.0;

            // Perpendicular direction (rotated 90° CCW from chord direction)
            double perpX = -dy / chord;
            double perpY =  dx / chord;

            // Distance from chord midpoint to arc centre
            double d = radius * Math.Cos(halfAngle);

            // Centre is offset in the perpendicular direction.
            // For a positive (CCW) bulge the centre is to the left of the chord direction.
            double cx = mx - perpX * d;
            double cy = my - perpY * d;

            double startAngle = Math.Atan2(start.Y - cy, start.X - cx);
            double endAngle   = Math.Atan2(end.Y   - cy, end.X   - cx);
            double sweep      = 2.0 * halfAngle; // signed sweep

            var coords = new List<Coordinate>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                double t     = (double)i / segments;
                double angle = startAngle + sweep * t;
                coords.Add(new Coordinate(cx + Math.Abs(radius) * Math.Cos(angle),
                                          cy + Math.Abs(radius) * Math.Sin(angle)));
            }
            return coords;
        }

        /// <summary>
        /// Tessellates a circular arc into line segments.
        /// </summary>
        private static List<Coordinate> TessellateArc(CircularArc2d arc, int segments = 16)
        {
            // GetSamplePoints returns evenly-spaced points from arc.StartPoint to arc.EndPoint
            // in the arc's own traversal direction — no angle/clockwise math required.
            Point2d[] pts = arc.GetSamplePoints(segments + 1);
            var coords = new List<Coordinate>(pts.Length);
            foreach (Point2d pt in pts)
                coords.Add(new Coordinate(pt.X, pt.Y));
            return coords;
        }

        /// <summary>
        /// Tessellates an elliptical arc into line segments.
        /// </summary>
        private static List<Coordinate> TessellateEllipse(EllipticalArc2d ellipse, int segments = 16)
        {
            // GetSamplePoints returns evenly-spaced points from ellipse.StartPoint to ellipse.EndPoint
            // in the ellipse's own traversal direction — no angle/clockwise math required.
            Point2d[] pts = ellipse.GetSamplePoints(segments + 1);
            var coords = new List<Coordinate>(pts.Length);
            foreach (Point2d pt in pts)
                coords.Add(new Coordinate(pt.X, pt.Y));
            return coords;
        }

        /// <summary>
        /// Gets the end point of a Curve2d.
        /// </summary>
        private static Point2d GetEndPoint(Curve2d curve)
        {
            if (curve is LineSegment2d line)
                return line.EndPoint;
            else if (curve is CircularArc2d arc)
                return arc.EndPoint;
            else if (curve is EllipticalArc2d ellipse)
                return ellipse.EndPoint;
            else
                return Point2d.Origin; // fallback
        }

        /// <summary>
        /// Computes signed area via the shoelace formula.
        /// Positive = CCW, Negative = CW. Only the magnitude is used for ring sorting.
        /// </summary>
        private static double ComputeSignedArea(List<Coordinate> coords)
        {
            double area = 0;
            int n = coords.Count;
            for (int i = 0; i < n - 1; i++)
                area += (coords[i].X * coords[i + 1].Y) - (coords[i + 1].X * coords[i].Y);
            return area / 2.0;
        }

        /// <summary>
        /// Computes the centroid of a ring using the shoelace-based formula.
        /// Works correctly for both CW and CCW rings.
        /// Falls back to vertex average for degenerate (zero-area) rings.
        /// </summary>
        private static Coordinate ComputeCentroid(List<Coordinate> coords)
        {
            double cx = 0, cy = 0;
            int n = coords.Count;
            for (int i = 0; i < n - 1; i++)
            {
                double cross = (coords[i].X * coords[i + 1].Y) - (coords[i + 1].X * coords[i].Y);
                cx += (coords[i].X + coords[i + 1].X) * cross;
                cy += (coords[i].Y + coords[i + 1].Y) * cross;
            }
            double area6 = 6.0 * ComputeSignedArea(coords);
            if (Math.Abs(area6) < 1e-10)
            {
                double sx = 0, sy = 0;
                foreach (var c in coords) { sx += c.X; sy += c.Y; }
                return new Coordinate(sx / n, sy / n);
            }
            return new Coordinate(cx / area6, cy / area6);
        }
    }
}
