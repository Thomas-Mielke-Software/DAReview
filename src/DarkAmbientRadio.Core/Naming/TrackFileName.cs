using System.Text.RegularExpressions;

namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// The Bandcamp track filename schema "Artist - Album - NN Title.mp3", split into its parts.
/// <para>
/// The track number is what anchors the split: it opens the title part. Neither the album nor
/// the title can be located by counting separators from one end, because <em>both</em> may
/// contain " - " themselves — compilations are named "Label - Album - NN TrackArtist - Title"
/// (taking the last separator was a bug: such albums parsed to zero tracks and silently
/// vanished from the review list).
/// </para>
/// </summary>
public sealed partial record TrackFileName(string Artist, string Album, string NumberedTitle, string Extension)
{
    private const string Separator = " - ";

    [GeneratedRegex(@"^(\d{1,3})\b")]
    private static partial Regex LeadingNumber();

    /// <summary>The track number opening <see cref="NumberedTitle"/>.</summary>
    public int? TrackNumber
    {
        get
        {
            var match = LeadingNumber().Match(NumberedTitle);
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
    }

    /// <summary>
    /// Returns null when the name carries no numbered-track segment (cover art, bonus images).
    /// </summary>
    public static TrackFileName? TryParse(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var segments = name.Split(Separator);
        if (segments.Length < 2)
            return null;

        // Scan left to right for the segment that starts with the track number. Start at 1:
        // segment 0 is always the artist, even if the artist name itself starts with digits.
        for (var i = 1; i < segments.Length; i++)
        {
            if (!LeadingNumber().IsMatch(segments[i]))
                continue;

            return new TrackFileName(
                Artist: segments[0],
                Album: string.Join(Separator, segments[1..i]),   // empty for "Artist - NN Title"
                NumberedTitle: string.Join(Separator, segments[i..]),
                Extension: extension);
        }

        return null;
    }

    public string ToFileName()
    {
        var stem = Album.Length == 0
            ? string.Join(Separator, Artist, NumberedTitle)
            : string.Join(Separator, Artist, Album, NumberedTitle);
        return stem + Extension;
    }
}

/// <summary>
/// The album folder name "Artist - Album". Everything after the first " - " is the album.
/// </summary>
public sealed record AlbumFolderName(string Artist, string Album)
{
    private const string Separator = " - ";

    /// <summary>Returns null for folders that carry no "Artist - Album" separator.</summary>
    public static AlbumFolderName? TryParse(string folderName)
    {
        var index = folderName.IndexOf(Separator, StringComparison.Ordinal);
        return index < 0
            ? null
            : new AlbumFolderName(folderName[..index], folderName[(index + Separator.Length)..]);
    }

    public override string ToString() => Artist + Separator + Album;
}
