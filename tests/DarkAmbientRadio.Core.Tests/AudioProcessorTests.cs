using DarkAmbientRadio.Core.Audio;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

/// <summary>
/// Only the checks that run <em>before</em> ffmpeg and mp3gain are exercised here — the encode
/// itself needs the external tools and is verified by hand against real albums.
/// </summary>
public class AudioProcessorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dar-audio-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _source;
    private readonly string _reviewDir;

    public AudioProcessorTests()
    {
        _source = Path.Combine(_root, "Some Artist - Some Album");
        _reviewDir = Path.Combine(_root, "review");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_reviewDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static AudioProcessor Processor() => new(null, null, "192k", -3);

    private void Write(string name, int bytes)
        => File.WriteAllBytes(Path.Combine(_source, name), new byte[bytes]);

    [Fact]
    public async Task ProcessAlbum_EmptyTrack_FailsWithATellingMessage()
    {
        Write("01 - present.mp3", 4096);
        Write("02 - hollow.mp3", 0);

        var ex = await Assert.ThrowsAsync<IOException>(
            () => Processor().ProcessAlbumAsync(_source, _reviewDir));

        Assert.Contains("0 Byte", ex.Message);
        Assert.Contains("02 - hollow.mp3", ex.Message);
        Assert.Contains("1 von 2", ex.Message);
    }

    /// <summary>Nothing is created for an album that cannot be processed.</summary>
    [Fact]
    public async Task ProcessAlbum_EmptyTrack_LeavesNoReviewFolder()
    {
        Write("01 - hollow.mp3", 0);

        await Assert.ThrowsAsync<IOException>(() => Processor().ProcessAlbumAsync(_source, _reviewDir));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_reviewDir));
    }

    /// <summary>An empty cover is not a reason to refuse the album; only the audio counts.</summary>
    [Fact]
    public async Task ProcessAlbum_EmptyCoverOnly_IsNotRefused()
    {
        Write("cover.jpg", 0);

        // No MP3 at all, so nothing calls ffmpeg — the empty jpg is simply copied over.
        var destination = await Processor().ProcessAlbumAsync(_source, _reviewDir);

        Assert.True(File.Exists(Path.Combine(destination, "cover.jpg")));
    }
}
