using Autodesk.AutoCAD.DatabaseServices;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Linq;

namespace C3DTools.Helpers
{
    public static class GeometryHelper
    {
        /// <summary>
        /// Replaces <paramref name="original"/> with one or more new polylines derived from
        /// <paramref name="fixedGeom"/>, copying all entity properties from the original.
        /// The original is erased. Returns the number of polylines created.
        /// </summary>
        public static int ReplaceWithFixedGeometry(Polyline original, Geometry fixedGeom, BlockTableRecord modelSpace, Transaction tr)
        {
            var plines = GeometriesToPolylines(FlattenGeometry(fixedGeom), original);
            if (plines.Count == 0) return 0;

            foreach (var pline in plines)
            {
                modelSpace.AppendEntity(pline);
                tr.AddNewlyCreatedDBObject(pline, true);
            }

            original.UpgradeOpen();
            original.Erase();

            return plines.Count;
        }

        /// <summary>
        /// Creates new polylines from <paramref name="geom"/>, copying entity properties
        /// from <paramref name="template"/>. Returns the number of polylines created.
        /// </summary>
        public static int CreatePolylines(Geometry geom, Polyline template, BlockTableRecord modelSpace, Transaction tr)
        {
            var plines = GeometriesToPolylines(FlattenGeometry(geom), template);

            foreach (var pline in plines)
            {
                modelSpace.AppendEntity(pline);
                tr.AddNewlyCreatedDBObject(pline, true);
            }

            return plines.Count;
        }

        /// <summary>
        /// Flattens a geometry (or geometry collection) into individual geometries.
        /// </summary>
        public static IEnumerable<Geometry> FlattenGeometry(Geometry geom)
        {
            if (geom is GeometryCollection col)
                return Enumerable.Range(0, col.NumGeometries).Select(col.GetGeometryN);
            return new[] { geom };
        }

        /// <summary>
        /// Recursively collects all non-empty <see cref="Polygon"/> instances from any geometry,
        /// filtering out degenerate polygons below <paramref name="minArea"/>.
        /// </summary>
        public static List<Polygon> FlattenPolygons(Geometry geom, double minArea = 1e-10)
        {
            var result = new List<Polygon>();
            if (geom is Polygon p)
            {
                if (!p.IsEmpty && p.Area > minArea) result.Add(p);
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    result.AddRange(FlattenPolygons(gc.GetGeometryN(i), minArea));
            }
            return result;
        }

        /// <summary>
        /// Converts a sequence of NTS geometries to polylines, copying entity properties
        /// from <paramref name="propertiesSource"/>. Unsupported geometry types are skipped.
        /// </summary>
        private static List<Polyline> GeometriesToPolylines(IEnumerable<Geometry> geometries, Polyline propertiesSource)
        {
            var result = new List<Polyline>();

            foreach (var geom in geometries.Where(g => g is Polygon || g is LineString))
            {
                var pline = GeometryConverter.NtsToPolyline(geom);
                if (pline == null) continue;

                pline.Layer      = propertiesSource.Layer;
                pline.Color      = propertiesSource.Color;
                pline.LineWeight = propertiesSource.LineWeight;
                pline.Linetype   = propertiesSource.Linetype;

                result.Add(pline);
            }

            return result;
        }
    }
}
