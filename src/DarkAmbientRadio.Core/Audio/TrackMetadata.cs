namespace DarkAmbientRadio.Core.Audio;

/// <summary>
/// The ID3 and stream facts about a single track, as shown next to the player.
/// Reading is best-effort: a missing, broken or locked file yields an empty instance
/// instead of an exception — this is display data, nothing depends on it.
/// </summary>
public sealed record TrackMetadata
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public uint Year { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Average bitrate in kbit/s (0 when unknown).</summary>
    public int Bitrate { get; init; }

    public int SampleRate { get; init; }
    public int Channels { get; init; }

    /// <summary>Track gain in dB as measured by mp3gain, or NaN when the file carries none.</summary>
    public double ReplayGainTrackGain { get; init; } = double.NaN;

    public static TrackMetadata Read(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(new SharedFileAbstraction(filePath));
            var tag = file.Tag;
            var properties = file.Properties;

            return new TrackMetadata
            {
                Title = Clean(tag.Title),
                // FirstPerformer is the track artist; on compilations the album artist differs.
                Artist = Clean(tag.FirstPerformer) ?? Clean(tag.FirstAlbumArtist),
                Album = Clean(tag.Album),
                Genre = Clean(tag.FirstGenre),
                Year = tag.Year,
                Duration = properties?.Duration ?? TimeSpan.Zero,
                Bitrate = properties?.AudioBitrate ?? 0,
                SampleRate = properties?.AudioSampleRate ?? 0,
                Channels = properties?.AudioChannels ?? 0,
                ReplayGainTrackGain = tag.ReplayGainTrackGain,
            };
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException
                                      or IOException or UnauthorizedAccessException)
        {
            return new TrackMetadata();
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
