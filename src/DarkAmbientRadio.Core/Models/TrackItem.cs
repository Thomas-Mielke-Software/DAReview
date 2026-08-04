using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Models;

/// <summary>A single audio track within an album folder.</summary>
public sealed class TrackItem
{
    public required string FilePath { get; init; }

    /// <summary>Unique within the album; assigned by <see cref="TrackNumbering"/>.</summary>
    public required int TrackNumber { get; init; }

    /// <summary>Which cascade step supplied <see cref="TrackNumber"/>.</summary>
    public TrackNumberSource NumberSource { get; init; } = TrackNumberSource.FileName;

    public TrackDecision Decision { get; set; } = TrackDecision.Undecided;

    public string FileName => Path.GetFileName(FilePath);

    public static TrackItem FromNumberedFile(NumberedTrackFile file) => new()
    {
        FilePath = file.FilePath,
        TrackNumber = file.Number,
        NumberSource = file.Source,
    };
}
