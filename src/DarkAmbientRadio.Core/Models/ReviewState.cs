using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkAmbientRadio.Core.Models;

/// <summary>
/// Per-album review state, stored as ".review.json" inside the album folder so it
/// travels with the folder through Nextcloud sync.
/// </summary>
public sealed class ReviewState
{
    public const string FileName = ".review.json";

    /// <summary>
    /// Cumulative count of tracks played to the end. Listen percentage is
    /// CompletedTrackPlays / TrackCount * 100 (100% = one full pass, 200% = two).
    /// </summary>
    public int CompletedTrackPlays { get; set; }

    /// <summary>Per-track decision, keyed by track number.</summary>
    public Dictionary<int, TrackDecision> Decisions { get; set; } = new();

    /// <summary>True once approved tracks have been published to the airplay folder.</summary>
    public bool Published { get; set; }

    /// <summary>The folder name (incl. any [OHNE/NUR TRACK ...] suffix) used on publish.</summary>
    public string? PublishedFolderName { get; set; }

    public int ListenPercent(int trackCount)
        => trackCount <= 0 ? 0 : (int)Math.Round(CompletedTrackPlays * 100.0 / trackCount);

    // ----- Persistence -------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ReviewState Load(string albumFolder)
    {
        var path = Path.Combine(albumFolder, FileName);
        if (!File.Exists(path))
            return new ReviewState();
        try
        {
            return JsonSerializer.Deserialize<ReviewState>(File.ReadAllText(path), JsonOptions)
                   ?? new ReviewState();
        }
        catch (JsonException)
        {
            return new ReviewState();
        }
    }

    public void Save(string albumFolder)
    {
        var path = Path.Combine(albumFolder, FileName);

        // A hidden file cannot be reopened with FileMode.Create, so clear the flag first.
        if (File.Exists(path))
        {
            var existing = File.GetAttributes(path);
            if (existing.HasFlag(FileAttributes.Hidden))
                File.SetAttributes(path, existing & ~FileAttributes.Hidden);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

        // Keep the sidecar from cluttering directory listings / broadcast scans.
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
        catch (IOException) { }
    }
}
