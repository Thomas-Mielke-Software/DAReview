using System.Text.RegularExpressions;

namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// Extracts the track number from an MP3 filename. Two shapes are understood:
/// the Bandcamp schema "Artist - Album - NN Title.mp3" (see <see cref="TrackFileName"/> for how
/// the number is located — it is not simply "after the last separator"), and a plain leading
/// number as used by every other source ("01 Title.mp3", "01_artist_-_title.mp3").
/// <para>
/// This is name-only and does no I/O; <see cref="TrackNumbering"/> wraps it with the ID3 tag and
/// positional fallbacks that make the review list independent of any naming convention.
/// </para>
/// </summary>
public static partial class TrackNumberParser
{
    // One to three digits at the very start, followed by a delimiter. The negative lookahead
    // keeps "2001 - Something.mp3" from yielding 200: a year is not a track number.
    [GeneratedRegex(@"^(\d{1,3})(?!\d)[\s._-]")]
    private static partial Regex LeadingNumber();

    /// <summary>
    /// Returns the parsed track number, or null when the file name carries none
    /// (e.g. cover art or bonus images).
    /// </summary>
    public static int? TryParse(string fileName)
        => TrackFileName.TryParse(fileName)?.TrackNumber ?? FromLeadingNumber(fileName);

    private static int? FromLeadingNumber(string fileName)
    {
        var match = LeadingNumber().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
