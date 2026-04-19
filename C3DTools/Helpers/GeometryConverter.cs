using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace C3DTools.Helpers
{
    public static class GeometryConverter
    {
        public static Geometry PolylineToNts(Polyline pline)
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
    }
}
