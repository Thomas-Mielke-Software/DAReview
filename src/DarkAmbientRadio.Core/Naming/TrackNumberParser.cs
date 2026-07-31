using System.Text.RegularExpressions;

namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// Extracts the track number from a Bandcamp MP3 filename of the shape
/// "Artist - Album - NN Title.mp3" (the NN after the last " - " separator).
/// </summary>
public static partial class TrackNumberParser
{
    [GeneratedRegex(@"^(\d{1,3})\b")]
    private static partial Regex LeadingNumber();

    /// <summary>
    /// Returns the parsed track number, or null when the file does not follow the
    /// numbered-track convention (e.g. cover art or bonus images).
    /// </summary>
    public static int? TryParse(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        int sep = name.LastIndexOf(" - ", StringComparison.Ordinal);
        var tail = sep >= 0 ? name[(sep + 3)..] : name;

        var match = LeadingNumber().Match(tail.TrimStart());
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
