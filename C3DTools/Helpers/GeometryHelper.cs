using Autodesk.AutoCAD.DatabaseServices;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.Linq;

namespace C3DTools.Helpers
{
    public static class GeometryHelper
    {
        public static int ReplaceWithFixedGeometry(Polyline original, Geometry fixedGeom, BlockTableRecord modelSpace, Transaction tr)
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
                GeometryConverter.SetPolylineGeometry(newPline, geometries[i]);

                // Add to model space
                modelSpace.AppendEntity(newPline);
                tr.AddNewlyCreatedDBObject(newPline, true);
            }

            // Delete the original polyline
            original.UpgradeOpen();
            original.Erase();

            return geometries.Count;
        }

        public static int CreatePolylines(Geometry geom, Polyline template, BlockTableRecord modelSpace, Transaction tr)
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
                GeometryConverter.SetPolylineGeometry(newPline, geometry);

                // Add to model space
                modelSpace.AppendEntity(newPline);
                tr.AddNewlyCreatedDBObject(newPline, true);
            }

            return geometries.Count;
        }
    }
}
