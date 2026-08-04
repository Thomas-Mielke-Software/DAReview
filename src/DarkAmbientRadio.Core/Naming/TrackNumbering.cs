using DarkAmbientRadio.Core.Audio;
using DarkAmbientRadio.Core.Files;

namespace DarkAmbientRadio.Core.Naming;

/// <summary>Where a track's number came from — the cascade step that produced it.</summary>
public enum TrackNumberSource
{
    /// <summary>Parsed out of the file name (Bandcamp schema or a leading number).</summary>
    FileName,

    /// <summary>Read from the ID3 track frame.</summary>
    Tag,

    /// <summary>Nothing said what number it is; it got its place in the folder listing.</summary>
    Position,
}

/// <param name="Number">Unique within the album, always ≥ 1.</param>
public readonly record struct NumberedTrackFile(string FilePath, int Number, TrackNumberSource Source);

/// <summary>
/// Numbers the tracks of one album folder, whatever they are called.
/// <para>
/// A track number is needed for three things only — the sort order, the decision keys in
/// ".review.json" and the "[OHNE TRACK 2 und 3]" suffix on publish — so it is derived rather than
/// demanded: file name → ID3 tag → position in the folder. Requiring the Bandcamp naming schema
/// instead meant that a hand-dropped album (scene releases name their files "01_artist_-_title.mp3")
/// produced zero tracks and was dropped from the review list without a word, even though the
/// pipeline had re-encoded and normalised it correctly.
/// </para>
/// </summary>
public static class TrackNumbering
{
    /// <summary>
    /// Assigns every file a unique number, in the order they should be reviewed. Files whose
    /// number is known keep it (gaps included — a rejected track leaves one); the rest fill the
    /// lowest free numbers in file-name order. Deterministic for a given set of files.
    /// </summary>
    public static IReadOnlyList<NumberedTrackFile> Assign(IEnumerable<string> filePaths)
    {
        var files = filePaths
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var numbers = new int?[files.Count];
        var sources = new TrackNumberSource[files.Count];

        for (var i = 0; i < files.Count; i++)
        {
            if (TrackNumberParser.TryParse(files[i]) is { } fromName)
            {
                numbers[i] = fromName;
                sources[i] = TrackNumberSource.FileName;
            }
            else if (ReadTagNumber(files[i]) is { } fromTag)
            {
                numbers[i] = fromTag;
                sources[i] = TrackNumberSource.Tag;
            }
        }

        // Claim the derived numbers first come, first served; a collision loses its claim and is
        // treated like a track that never had a number.
        var taken = new HashSet<int>();
        for (var i = 0; i < files.Count; i++)
        {
            if (numbers[i] is { } number && !taken.Add(number))
                numbers[i] = null;
        }

        var next = 1;
        for (var i = 0; i < files.Count; i++)
        {
            if (numbers[i] is not null)
                continue;

            while (!taken.Add(next))
                next++;

            numbers[i] = next;
            sources[i] = TrackNumberSource.Position;
        }

        return files
            .Select((path, i) => new NumberedTrackFile(path, numbers[i]!.Value, sources[i]))
            .ToList();
    }

    private static int? ReadTagNumber(string filePath)
    {
        // Never pull a cloud placeholder down for this. The review list is rescanned on every
        // refresh, and an app-triggered on-demand download is exactly what gets the app blocked
        // (see CloudFiles) — a track whose content is not here yet falls through to its position.
        if (CloudFiles.IsPlaceholder(filePath))
            return null;

        try
        {
            // ReadStyle.None: the track frame is wanted, not the audio properties.
            using var file = TagLib.File.Create(new SharedFileAbstraction(filePath), TagLib.ReadStyle.None);
            var track = file.Tag.Track;
            return track is > 0 and <= 999 ? (int)track : null;
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                      or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
