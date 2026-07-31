using DarkAmbientRadio.Core.Models;

namespace DarkAmbientRadio.Core.Review;

/// <summary>
/// Centralises mutations of an album's <see cref="ReviewState"/> and persists the
/// sidecar file after each change.
/// </summary>
public sealed class ReviewStore
{
    /// <summary>Records a per-track approve/reject decision and persists it.</summary>
    public void SetDecision(Album album, TrackItem track, TrackDecision decision)
    {
        track.Decision = decision;
        album.State.Decisions[track.TrackNumber] = decision;
        album.State.Save(album.FolderPath);
    }

    /// <summary>Increments the completed-track-play counter (called when a track ends).</summary>
    public void RecordTrackPlayed(Album album)
    {
        album.State.CompletedTrackPlays++;
        album.State.Save(album.FolderPath);
    }

    /// <summary>Marks the album as published to the airplay folder.</summary>
    public void MarkPublished(Album album, string publishedFolderName)
    {
        album.State.Published = true;
        album.State.PublishedFolderName = publishedFolderName;
        album.State.Save(album.FolderPath);
    }
}
