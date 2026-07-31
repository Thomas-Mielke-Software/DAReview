using DarkAmbientRadio.Core.Airplay;
using DarkAmbientRadio.Core.Library;
using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Review;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class AirplayIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _reviewDir;
    private readonly string _airplayDir;

    public AirplayIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dar_test_" + Guid.NewGuid().ToString("N"));
        _reviewDir = Path.Combine(_root, "Review");
        _airplayDir = Path.Combine(_root, "Airplay");
        Directory.CreateDirectory(_reviewDir);
        Directory.CreateDirectory(_airplayDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string CreateAlbum(string name, int trackCount)
    {
        var folder = Path.Combine(_reviewDir, name);
        Directory.CreateDirectory(folder);
        for (int i = 1; i <= trackCount; i++)
            File.WriteAllText(Path.Combine(folder, $"Artist - {name} - {i:00} Track {i}.mp3"), "dummy");
        File.WriteAllText(Path.Combine(folder, "cover.jpg"), "img");
        return folder;
    }

    [Fact]
    public void Library_scans_albums_and_tracks()
    {
        CreateAlbum("Test Album", 3);

        var albums = new AlbumLibrary().LoadReviewQueue(_reviewDir);

        var album = Assert.Single(albums);
        Assert.Equal("Test Album", album.Name);
        Assert.Equal(3, album.Tracks.Count);
        Assert.Equal(new[] { 1, 2, 3 }, album.Tracks.Select(t => t.TrackNumber));
    }

    [Fact]
    public void Publish_copies_only_approved_tracks_with_renamed_folder()
    {
        CreateAlbum("Test Album", 3);
        var store = new ReviewStore();
        var album = new AlbumLibrary().LoadReviewQueue(_reviewDir).Single();

        store.SetDecision(album, album.Tracks[0], TrackDecision.Approved); // 1
        store.SetDecision(album, album.Tracks[1], TrackDecision.Rejected); // 2
        store.SetDecision(album, album.Tracks[2], TrackDecision.Approved); // 3
        Assert.True(album.AllTracksDecided);

        var result = new AirplayPublisher().Publish(album, _airplayDir);

        Assert.Equal("Test Album [OHNE TRACK 2]", result.FolderName);
        Assert.Equal(2, result.TrackCount);

        var produced = Directory.GetFiles(result.DestinationFolder).Select(Path.GetFileName).ToList();
        Assert.Contains("Artist - Test Album - 01 Track 1.mp3", produced);
        Assert.Contains("Artist - Test Album - 03 Track 3.mp3", produced);
        Assert.DoesNotContain("Artist - Test Album - 02 Track 2.mp3", produced);
        Assert.Contains("cover.jpg", produced); // cover art carried over
    }

    [Fact]
    public void Listen_percent_reflects_completed_track_plays()
    {
        CreateAlbum("Test Album", 4);
        var store = new ReviewStore();
        var album = new AlbumLibrary().LoadReviewQueue(_reviewDir).Single();

        // Two full passes of a 4-track album -> 8 completed plays -> 200 %.
        for (int i = 0; i < 8; i++)
            store.RecordTrackPlayed(album);

        Assert.Equal(200, album.ListenPercent);

        // Reload from disk to confirm persistence.
        var reloaded = new AlbumLibrary().LoadReviewQueue(_reviewDir).Single();
        Assert.Equal(200, reloaded.ListenPercent);
    }

    [Fact]
    public void Published_albums_are_excluded_from_the_queue()
    {
        CreateAlbum("Test Album", 2);
        var store = new ReviewStore();
        var album = new AlbumLibrary().LoadReviewQueue(_reviewDir).Single();
        store.SetDecision(album, album.Tracks[0], TrackDecision.Approved);
        store.SetDecision(album, album.Tracks[1], TrackDecision.Approved);
        store.MarkPublished(album, "Test Album");

        Assert.Empty(new AlbumLibrary().LoadReviewQueue(_reviewDir));
        Assert.Single(new AlbumLibrary().LoadReviewQueue(_reviewDir, includePublished: true));
    }
}
