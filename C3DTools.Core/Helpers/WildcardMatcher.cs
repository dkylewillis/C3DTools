namespace C3DTools.Core.Helpers
{
    /// <summary>
    /// Case-insensitive wildcard matching supporting * and ? patterns.
    /// </summary>
    public static class WildcardMatcher
    {
        public static bool IsMatch(string pattern, string input)
        {
            return MatchSpan(pattern.AsSpan(), input.AsSpan());
        }

        private static bool MatchSpan(ReadOnlySpan<char> pattern, ReadOnlySpan<char> input)
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
                        if (MatchSpan(pattern, input.Slice(i)))
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
