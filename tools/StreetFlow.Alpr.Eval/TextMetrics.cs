namespace StreetFlow.Alpr.Eval;

public static class TextMetrics
{
    /// <summary>
    /// Character-level accuracy of a reading vs. the expected text:
    /// 1 − normalized Levenshtein distance. Empty expected + empty read = 1.
    /// </summary>
    public static double CharacterAccuracy(string expected, string actual)
    {
        if (expected.Length == 0 && actual.Length == 0)
            return 1.0;
        var distance = Levenshtein(expected, actual);
        return 1.0 - (double)distance / Math.Max(expected.Length, actual.Length);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitutionCost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitutionCost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
