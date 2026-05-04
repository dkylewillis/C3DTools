using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using System.Collections.Generic;
using System.Linq;

namespace C3DTools.Helpers
{
    /// <summary>
    /// Thin wrappers around NTS boolean operations shared across commands.
    /// Uses <see cref="OverlayNGRobust"/> on original coordinates first to preserve
    /// arc-tessellated vertex positions. Only if that throws a <see cref="TopologyException"/>
    /// are inputs repaired with <see cref="GeometryFixer"/> and retried once.
    ///
    /// Before any overlay, inputs are normalised to areal-only geometry so that
    /// mixed-dimension <see cref="GeometryCollection"/>s (lines/points mixed with
    /// polygons) do not cause "Overlay input is mixed-dimension" exceptions.
    /// </summary>
    public static class BooleanOperationHelper
    {
        /// <summary>Returns the intersection of <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static Geometry Intersect(Geometry a, Geometry b)
            => Overlay(ToAreal(a), ToAreal(b), OverlayNG.INTERSECTION);

        /// <summary>Returns the difference of <paramref name="a"/> minus <paramref name="b"/>.</summary>
        public static Geometry Difference(Geometry a, Geometry b)
            => Overlay(ToAreal(a), ToAreal(b), OverlayNG.DIFFERENCE);

        /// <summary>Returns the union of <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static Geometry Union(Geometry a, Geometry b)
            => Overlay(ToAreal(a), ToAreal(b), OverlayNG.UNION);

        // ── Private helpers ───────────────────────────────────────────────────────

        private static Geometry Overlay(Geometry a, Geometry b, SpatialFunction op)
        {
            try
            {
                return OverlayNGRobust.Overlay(a, b, op);
            }
            catch (TopologyException)
            {
                // GeometryFixer may slightly alter tessellated arc vertices, so it is
                // intentionally skipped on the first attempt to preserve geometry fidelity.
                // Only used here as a last resort when the overlay cannot proceed.
                return OverlayNGRobust.Overlay(
                    GeometryFixer.Fix(a) ?? a,
                    GeometryFixer.Fix(b) ?? b,
                    op);
            }
        }

        /// <summary>
        /// Extracts only the areal (2-D polygon) components from <paramref name="geom"/>.
        /// This prevents "Overlay input is mixed-dimension" exceptions when a geometry
        /// collection contains a mix of polygons, line strings, or points — which can
        /// occur with hatches converted via <see cref="GeometryConverter.HatchToNts"/> or
        /// with the output of previous overlay operations.
        ///
        /// Returns a <see cref="Polygon"/>, <see cref="MultiPolygon"/>, or
        /// <see cref="GeometryCollection.Empty"/> if no polygons are present.
        /// </summary>
        private static Geometry ToAreal(Geometry geom)
        {
            if (geom is Polygon)
                return geom;

            if (geom is MultiPolygon)
                return geom;

            // Recursively collect all Polygon components
            var polygons = new List<Polygon>();
            CollectPolygons(geom, polygons);

            if (polygons.Count == 0)
                return geom.Factory.CreateEmpty(Dimension.Surface);

            if (polygons.Count == 1)
                return polygons[0];

            return geom.Factory.CreateMultiPolygon(polygons.ToArray());
        }

        private static void CollectPolygons(Geometry geom, List<Polygon> result)
        {
            if (geom is Polygon p)
            {
                result.Add(p);
            }
            else if (geom is GeometryCollection gc)
            {
                for (int i = 0; i < gc.NumGeometries; i++)
                    CollectPolygons(gc.GetGeometryN(i), result);
            }
        }
    }
}
