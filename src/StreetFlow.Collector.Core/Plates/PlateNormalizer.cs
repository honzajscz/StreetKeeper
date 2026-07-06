using System.Text;

namespace StreetFlow.Collector.Core.Plates;

/// <summary>
/// Normalizes a raw OCR plate reading into a canonical form so the same car
/// always produces the same hash (spec §4 of the PRD, §3.5 of the surface design):
/// uppercase, strip separators/whitespace, unify OCR-confusable characters.
/// Pure function, developed TDD-first.
/// </summary>
public static class PlateNormalizer
{
    /// <summary>
    /// Characters that mobile OCR routinely confuses, mapped to one canonical
    /// representative. Letters map to the digit they are confused with
    /// (O↔0, I↔1, …) so that either reading lands on the same normalized plate.
    /// </summary>
    private static readonly IReadOnlyDictionary<char, char> ConfusableMap = new Dictionary<char, char>
    {
        ['O'] = '0',
        ['Q'] = '0',
        ['D'] = '0',
        ['I'] = '1',
        ['L'] = '1',
    };

    /// <summary>
    /// Normalizes a raw plate reading: uppercases, removes everything that is not
    /// A–Z/0–9 (spaces, dashes, dots, frame artifacts) and folds confusable
    /// characters to a canonical form. Returns an empty string when nothing
    /// plate-like remains.
    /// </summary>
    public static string Normalize(string? rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate))
            return string.Empty;

        var builder = new StringBuilder(rawPlate.Length);
        foreach (var ch in rawPlate)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper is < '0' or > 'Z' || (upper > '9' && upper < 'A'))
                continue; // not A–Z / 0–9 → separator or OCR noise, drop it

            builder.Append(ConfusableMap.TryGetValue(upper, out var canonical) ? canonical : upper);
        }

        return builder.ToString();
    }
}
