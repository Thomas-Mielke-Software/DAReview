using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DarkAmbientRadio.Core.Audio;

namespace DarkAmbientRadio.App.ViewModels;

/// <summary>One labelled fact in the info strip; <paramref name="IsWarning"/> paints it amber.</summary>
public sealed record TrackInfoChip(string Label, string Value, bool IsWarning = false);

/// <summary>
/// Formats the ID3/stream facts of the playing track for the strip between player and
/// track list. Facts the file does not carry are simply left out.
/// </summary>
public sealed class TrackInfoViewModel
{
    /// <summary>Tolerance when comparing against the configured target bitrate (VBR/LAME jitter).</summary>
    private const int BitrateTolerance = 8;

    public TrackInfoViewModel(TrackMetadata metadata, string filePath, int expectedBitrate)
    {
        // The tag title is what listeners will see; the filename is only the fallback.
        Title = metadata.Title ?? Path.GetFileNameWithoutExtension(filePath);
        Chips = BuildChips(metadata, expectedBitrate).ToList();
    }

    public string Title { get; }
    public IReadOnlyList<TrackInfoChip> Chips { get; }

    private static IEnumerable<TrackInfoChip> BuildChips(TrackMetadata m, int expectedBitrate)
    {
        if (m.Artist is not null)
            yield return new TrackInfoChip("Artist", m.Artist);

        if (m.Album is not null)
            yield return new TrackInfoChip("Album", m.Album);

        if (m.Year > 0)
            yield return new TrackInfoChip("Jahr", m.Year.ToString(CultureInfo.CurrentCulture));

        if (m.Genre is not null)
            yield return new TrackInfoChip("Genre", m.Genre);

        if (m.Duration > TimeSpan.Zero)
            yield return new TrackInfoChip("Länge", FormatDuration(m.Duration));

        if (m.Bitrate > 0)
        {
            // Folder imports skip the re-encode, so a 320k album can reach the review queue —
            // flag anything off-target instead of letting it slip through to airplay.
            var offTarget = expectedBitrate > 0 && Math.Abs(m.Bitrate - expectedBitrate) > BitrateTolerance;
            yield return new TrackInfoChip("Bitrate", $"{m.Bitrate} kbit/s", offTarget);
        }

        if (m.SampleRate > 0)
            yield return new TrackInfoChip("Format", FormatStream(m.SampleRate, m.Channels));

        if (!double.IsNaN(m.ReplayGainTrackGain))
            yield return new TrackInfoChip(
                "Track Gain", m.ReplayGainTrackGain.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB");
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static string FormatStream(int sampleRate, int channels)
    {
        var khz = (sampleRate / 1000.0).ToString("0.###", CultureInfo.CurrentCulture) + " kHz";
        return channels switch
        {
            1 => khz + " · Mono",
            2 => khz + " · Stereo",
            > 2 => $"{khz} · {channels} Kanäle",
            _ => khz,
        };
    }
}
