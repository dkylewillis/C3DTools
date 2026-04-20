using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace C3DTools.Helpers
{
    /// <summary>
    /// Thin wrappers around NTS boolean operations shared across commands.
    /// Uses <see cref="OverlayNGRobust"/> on original coordinates first to preserve
    /// arc-tessellated vertex positions. Only if that throws a <see cref="TopologyException"/>
    /// are inputs repaired with <see cref="GeometryFixer"/> and retried once.
    /// </summary>
    public static class BooleanOperationHelper
    {
        /// <summary>Returns the intersection of <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static Geometry Intersect(Geometry a, Geometry b)
            => Overlay(a, b, OverlayNG.INTERSECTION);

        /// <summary>Returns the difference of <paramref name="a"/> minus <paramref name="b"/>.</summary>
        public static Geometry Difference(Geometry a, Geometry b)
            => Overlay(a, b, OverlayNG.DIFFERENCE);

        /// <summary>Returns the union of <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static Geometry Union(Geometry a, Geometry b)
            => Overlay(a, b, OverlayNG.UNION);

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
    }
}
