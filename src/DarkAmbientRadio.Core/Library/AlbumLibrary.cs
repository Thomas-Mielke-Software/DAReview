using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Library;

/// <summary>Scans the review directory and materialises <see cref="Album"/> objects.</summary>
public sealed class AlbumLibrary
{
    /// <summary>
    /// Loads all reviewable albums from <paramref name="reviewDir"/>. Folders whose name
    /// starts with "!" (e.g. "!Free") and folders without a single MP3 are skipped — how the
    /// MP3s are named is <em>not</em> a criterion, see <see cref="TrackNumbering"/>.
    /// Already-published albums are excluded unless <paramref name="includePublished"/> is set.
    /// </summary>
    public IReadOnlyList<Album> LoadReviewQueue(string reviewDir, bool includePublished = false)
    {
        if (!Directory.Exists(reviewDir))
            return Array.Empty<Album>();

        var albums = new List<Album>();
        foreach (var dir in Directory.EnumerateDirectories(reviewDir))
        {
            var folderName = Path.GetFileName(dir);
            if (folderName.StartsWith('!'))
                continue;

            var state = ReviewState.Load(dir);
            if (state.Published && !includePublished)
                continue;

            var tracks = TrackNumbering.Assign(Directory.EnumerateFiles(dir, "*.mp3"))
                .Select(TrackItem.FromNumberedFile)
                .OrderBy(t => t.TrackNumber)
                .ToList();

            if (tracks.Count == 0)
                continue;

            foreach (var track in tracks)
            {
                if (state.Decisions.TryGetValue(track.TrackNumber, out var decision))
                    track.Decision = decision;
            }

            albums.Add(new Album { FolderPath = dir, Tracks = tracks, State = state });
        }

        return albums.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
