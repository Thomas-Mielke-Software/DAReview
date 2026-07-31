namespace DarkAmbientRadio.Core.Models;

/// <summary>An album folder in the review queue, with its tracks and review state.</summary>
public sealed class Album
{
    public required string FolderPath { get; init; }
    public required IReadOnlyList<TrackItem> Tracks { get; init; }
    public required ReviewState State { get; init; }

    public string Name => Path.GetFileName(FolderPath);

    public int ListenPercent => State.ListenPercent(Tracks.Count);

    public bool AllTracksDecided => Tracks.Count > 0 && Tracks.All(t => t.Decision != TrackDecision.Undecided);

    public IEnumerable<TrackItem> ApprovedTracks => Tracks.Where(t => t.Decision == TrackDecision.Approved);
    public IEnumerable<TrackItem> RejectedTracks => Tracks.Where(t => t.Decision == TrackDecision.Rejected);
}
