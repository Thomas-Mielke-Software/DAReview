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

}
