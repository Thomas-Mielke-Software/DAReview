namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// Extracts the track number from a Bandcamp MP3 filename of the shape
/// "Artist - Album - NN Title.mp3". See <see cref="TrackFileName"/> for how the number is
/// located — it is not simply "after the last separator".
/// </summary>
public static class TrackNumberParser
{
    /// <summary>
    /// Returns the parsed track number, or null when the file does not follow the
    /// numbered-track convention (e.g. cover art or bonus images).
    /// </summary>
    public static int? TryParse(string fileName)
        => TrackFileName.TryParse(fileName)?.TrackNumber;
}
