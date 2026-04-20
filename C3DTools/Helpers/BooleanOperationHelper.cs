using NetTopologySuite.Geometries;

namespace C3DTools.Helpers
{
    /// <summary>
    /// Thin wrappers around NTS boolean operations shared across commands.
    /// </summary>
    public static class BooleanOperationHelper
    {
        /// <summary>
        /// Returns the intersection of <paramref name="a"/> and <paramref name="b"/>.
        /// Returns an empty geometry when the two geometries do not overlap.
        /// </summary>
        public static Geometry Intersect(Geometry a, Geometry b)
        {
            return a.Intersection(b);
        }

        /// <summary>
        /// Returns the difference of <paramref name="a"/> minus <paramref name="b"/>.
        /// Returns an empty geometry when <paramref name="b"/> fully covers <paramref name="a"/>.
        /// </summary>
        public static Geometry Difference(Geometry a, Geometry b)
        {
            return a.Difference(b);
        }
    }
}
