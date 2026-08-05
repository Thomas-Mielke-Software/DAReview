using DarkAmbientRadio.Core.Audio;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class TrackMetadataTests
{
    // The info strip is decoration: whatever the review folder throws at it — a placeholder
    // that never hydrated, a stub file, a track renamed mid-read — it must not throw.
    [Fact]
    public void Reading_a_missing_file_yields_empty_metadata()
    {
        var metadata = TrackMetadata.Read(
            Path.Combine(Path.GetTempPath(), "dar_missing_" + Guid.NewGuid().ToString("N") + ".mp3"));

        Assert.Null(metadata.Title);
        Assert.Equal(0, metadata.Bitrate);
        Assert.True(double.IsNaN(metadata.ReplayGainTrackGain));
    }

    [Fact]
    public void Reading_a_file_that_is_not_audio_yields_empty_metadata()
    {
        var path = Path.Combine(Path.GetTempPath(), "dar_bogus_" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllText(path, "not an mp3");
        try
        {
            var metadata = TrackMetadata.Read(path);

            Assert.Null(metadata.Title);
            Assert.Equal(TimeSpan.Zero, metadata.Duration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Only_tracks_under_the_threshold_are_named()
    {
        using var album = new TempFolder();
        album.Write("01.mp3", Mp3Frames.Track(192, 40));
        album.Write("02.mp3", Mp3Frames.Track(128, 40));
        album.Write("03.mp3", Mp3Frames.Track(160, 40));   // exactly at the threshold: fine
        album.Write("04.mp3", Mp3Frames.Track(96, 40));

        Assert.Equal(
            [new TrackBitrate("02.mp3", 128), new TrackBitrate("04.mp3", 96)],
            TrackMetadata.FindTracksBelow(album.Path, 160));
    }

    [Fact]
    public void A_track_whose_bitrate_cannot_be_read_is_not_reported_as_too_low()
    {
        // "Don't know" must not turn into a warning — this only ever feeds a quality complaint.
        using var album = new TempFolder();
        album.Write("01.mp3", "not an mp3");

        Assert.Empty(TrackMetadata.FindTracksBelow(album.Path, 160));
        Assert.Empty(TrackMetadata.FindTracksBelow(Path.Combine(album.Path, "gone"), 160));
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dar-meta-" + Guid.NewGuid().ToString("N")[..8]);

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Write(string name, byte[] content)
            => File.WriteAllBytes(System.IO.Path.Combine(Path, name), content);

        public void Write(string name, string content)
            => File.WriteAllText(System.IO.Path.Combine(Path, name), content);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
