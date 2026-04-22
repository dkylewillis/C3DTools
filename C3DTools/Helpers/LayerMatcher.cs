using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;

namespace C3DTools.Helpers
{
    /// <summary>
    /// Matches layer names in a drawing against wildcard patterns (* and ?).
    /// </summary>
    public static class LayerMatcher
    {
        /// <summary>
        /// Returns all layer names in the drawing that match at least one of the given patterns.
        /// Matching is case-insensitive. Supports * (any sequence) and ? (any single character).
        /// Patterns that match no layers are collected in unmatchedPatterns.
        /// </summary>
        public static IReadOnlyList<string> GetMatchingLayers(
            Database db,
            IEnumerable<string> patterns,
            out List<string> unmatchedPatterns)
        {
            var allLayers = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    allLayers.Add(ltr.Name);
                }
                tr.Commit();
            }

            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            unmatchedPatterns = new List<string>();

            foreach (string pattern in patterns)
            {
                bool anyMatch = false;
                foreach (string layer in allLayers)
                {
                    if (WildcardMatch(pattern, layer))
                    {
                        matched.Add(layer);
                        anyMatch = true;
                    }
                }
                if (!anyMatch)
                    unmatchedPatterns.Add(pattern);
            }

            return new List<string>(matched);
        }

        /// <summary>
        /// Case-insensitive wildcard match supporting * and ?.
        /// </summary>
        public static bool WildcardMatch(string pattern, string input)
        {
            return WildcardMatchSpan(pattern.AsSpan(), input.AsSpan());
        }

        private static bool WildcardMatchSpan(ReadOnlySpan<char> pattern, ReadOnlySpan<char> input)
        {
            while (pattern.Length > 0 && input.Length > 0)
            {
                if (pattern[0] == '*')
                {
                    pattern = pattern.Slice(1);
                    while (pattern.Length > 0 && pattern[0] == '*')
                        pattern = pattern.Slice(1);

                    if (pattern.Length == 0) return true;

                    for (int i = 0; i <= input.Length; i++)
                    {
                        if (WildcardMatchSpan(pattern, input.Slice(i)))
                            return true;
                    }
                    return false;
                }

                if (pattern[0] == '?' || char.ToUpperInvariant(pattern[0]) == char.ToUpperInvariant(input[0]))
                {
                    pattern = pattern.Slice(1);
                    input = input.Slice(1);
                }
                else
                {
                    return false;
                }
            }

            while (pattern.Length > 0 && pattern[0] == '*')
                pattern = pattern.Slice(1);

            return pattern.Length == 0 && input.Length == 0;
        }
    }
}
