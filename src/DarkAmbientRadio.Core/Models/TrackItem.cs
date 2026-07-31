using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Models;

/// <summary>A single audio track within an album folder.</summary>
public sealed class TrackItem
{
    public required string FilePath { get; init; }
    public required int TrackNumber { get; init; }
    public TrackDecision Decision { get; set; } = TrackDecision.Undecided;

    public string FileName => Path.GetFileName(FilePath);

    public static TrackItem? FromFile(string filePath)
    {
        var number = TrackNumberParser.TryParse(filePath);
        return number is null
            ? null
            : new TrackItem { FilePath = filePath, TrackNumber = number.Value };
    }
}
