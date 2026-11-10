using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Library;

/// <summary>Scans the review directory and materialises <see cref="Album"/> objects.</summary>
public sealed class AlbumLibrary
{
    /// <summary>
    /// Asked when the review directory itself cannot be read, with the path and the exception;
    /// true retries, false gives up and yields an empty queue. In practice this is a Nextcloud
    /// sync root whose client has not started yet: the folder is there as a reparse point, so
    /// <see cref="Directory.Exists(string)"/> says yes, and the enumeration then dies with
    /// <em>"Es wurde eine ungültige Namensanforderung gestellt"</em> (ERROR_INVALID_NAME) —
    /// nothing a restart of the app would fix, but starting the client and retrying does.
    /// <para>
    /// Left unset — in the tests, and anywhere else without a UI — the exception is rethrown, so
    /// a genuinely broken path still surfaces instead of turning into a silently empty list.
    /// Core carries no UI reference (it is plain <c>net9.0</c>), hence the callback; the WPF side
    /// hangs its Wiederholen/Abbrechen box in here.
    /// </para>
    /// </summary>
    public Func<string, Exception, bool>? DirectoryUnavailable { get; set; }

    /// <summary>
    /// Loads all reviewable albums from <paramref name="reviewDir"/>. Folders whose name
    /// starts with "!" (e.g. "!Free") and folders without a single MP3 are skipped — how the
    /// MP3s are named is <em>not</em> a criterion, see <see cref="TrackNumbering"/>.
    /// Already-published albums are excluded unless <paramref name="includePublished"/> is set.
    /// </summary>
    public IReadOnlyList<Album> LoadReviewQueue(string reviewDir, bool includePublished = false)
    {
        var albums = new List<Album>();
        foreach (var dir in ListAlbumFolders(reviewDir))
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

    /// <summary>
    /// The album folders, retried for as long as <see cref="DirectoryUnavailable"/> asks for it.
    /// <para>
    /// <see cref="Directory.GetDirectories(string)"/>, not <c>EnumerateDirectories</c>: the lazy
    /// one hands back an iterator and only touches the file system on the first <c>MoveNext</c>,
    /// i.e. inside the caller's <c>foreach</c> — a <c>try</c> around the call itself would never
    /// see the exception, and a retry could not restart a half-consumed enumeration anyway.
    /// </para>
    /// A directory that is simply absent stays what it always was: an empty queue, no dialog.
    /// </summary>
    private string[] ListAlbumFolders(string reviewDir)
    {
        while (true)
        {
            try
            {
                return Directory.Exists(reviewDir)
                    ? Directory.GetDirectories(reviewDir)
                    : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DirectoryUnavailable is null)
                    throw;
                if (!DirectoryUnavailable(reviewDir, ex))
                    return Array.Empty<string>();
            }
        }
    }
}
