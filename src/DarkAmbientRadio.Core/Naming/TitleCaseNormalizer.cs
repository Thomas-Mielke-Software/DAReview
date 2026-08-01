using System.Text;
using System.Text.RegularExpressions;

namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// Normalises the capitalisation of album/artist/title strings that arrive from Bandcamp in
/// inconsistent shapes ("ETERNAL VOID", "eternal void"). Deliberately conservative: a word
/// that is already mixed-case ("McCoy", "iVoid", "DiN") is left alone, so only the obviously
/// unnormalised all-caps / all-lower words are touched.
/// </summary>
public static partial class TitleCaseNormalizer
{
    /// <summary>Words kept lowercase unless they open or close the string.</summary>
    private static readonly HashSet<string> MinorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into", "nor", "of",
        "on", "onto", "or", "over", "the", "to", "under", "up", "upon", "vs", "with", "within",
    };

    /// <summary>Roman numerals ("Part III") must survive as uppercase.</summary>
    [GeneratedRegex(@"^[IVXLCDM]+$")]
    private static partial Regex RomanNumeral();

    /// <summary>Splits into words plus the separators between them, so spacing is preserved.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();

    /// <summary>
    /// Returns <paramref name="text"/> in title case. Returns the input unchanged when it is
    /// null/whitespace, so callers can feed it unparsed filename fragments safely.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var matches = Word().Matches(text);
        if (matches.Count == 0)
            return text;

        var result = new StringBuilder(text);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var isEdge = i == 0 || i == matches.Count - 1;
            var normalised = NormalizeWord(match.Value, isEdge);
            if (normalised != match.Value)
                result.Remove(match.Index, match.Length).Insert(match.Index, normalised);
        }

        return result.ToString();
    }

    private static string NormalizeWord(string word, bool isEdge)
    {
        var upper = word.ToUpperInvariant();
        var lower = word.ToLowerInvariant();

        // Mixed case is assumed intentional — don't second-guess it.
        if (word != upper && word != lower)
            return word;

        if (RomanNumeral().Match(upper).Success && word == upper)
            return word;

        if (!isEdge && MinorWords.Contains(word))
            return lower;

        return Capitalize(lower);
    }

    private static string Capitalize(string lowerWord)
        => char.ToUpperInvariant(lowerWord[0]) + lowerWord[1..];
}
