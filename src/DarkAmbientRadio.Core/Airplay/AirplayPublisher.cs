using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Airplay;

/// <param name="ReviewFolderMoved">
/// True when the whole review folder was handed over instead of copied — the caller then has
/// nothing left to clean up, and the album no longer exists at its old path.
/// </param>
public sealed record PublishResult(
    string DestinationFolder,
    string FolderName,
    int TrackCount,
    bool ReviewFolderMoved);

/// <summary>
/// Publishes the approved tracks of an album into the airplay folder, renaming the
/// folder with an [OHNE TRACK ...] / [NUR TRACK ...] suffix when tracks were rejected.
/// The MP3 320 archive is never touched.
/// </summary>
public sealed class AirplayPublisher
{
    private readonly string _connectorOhne;
    private readonly string _connectorNur;

    public AirplayPublisher(string connectorOhne = "und", string connectorNur = "UND")
    {
        _connectorOhne = connectorOhne;
        _connectorNur = connectorNur;
    }

    /// <summary>Computes the destination folder name (album name plus any suffix).</summary>
    public string BuildFolderName(Album album)
    {
        var suffix = TrackListFormatter.BuildSuffix(
            album.Tracks.Select(t => t.TrackNumber),
            album.RejectedTracks.Select(t => t.TrackNumber),
            _connectorOhne,
            _connectorNur);
        return album.Name + suffix;
    }

    /// <summary>
    /// Copies approved track MP3s plus cover art / images into &lt;airplayDir&gt;\&lt;folder name&gt;.
    /// Rejected audio is omitted; the review sidecar is not copied.
    /// <para>
    /// When the album was approved as a whole there is nothing to leave behind, and the review
    /// folder is <em>moved</em> instead — see <see cref="TryMove"/>.
    /// </para>
    /// </summary>
    public PublishResult Publish(Album album, string airplayDir)
    {
        var approved = album.ApprovedTracks.ToList();
        if (approved.Count == 0)
            throw new InvalidOperationException("Keine freigegebenen Tracks – nichts zum Ausstrahlen.");

        var folderName = BuildFolderName(album);
        var destination = Path.Combine(airplayDir, folderName);

        // Only with every single track approved: an undecided one would otherwise ride along.
        if (approved.Count == album.Tracks.Count && TryMove(album.FolderPath, destination))
            return new PublishResult(destination, folderName, approved.Count, ReviewFolderMoved: true);

        Directory.CreateDirectory(destination);

        // Approved audio (original file names, numbering gaps preserved).
        foreach (var track in approved)
            File.Copy(track.FilePath, Path.Combine(destination, track.FileName), overwrite: true);

        // Non-audio assets (cover art etc.), excluding the review sidecar.
        foreach (var file in Directory.EnumerateFiles(album.FolderPath))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(ReviewState.FileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destination, name), overwrite: true);
        }

        return new PublishResult(destination, folderName, approved.Count, ReviewFolderMoved: false);
    }

    /// <summary>
    /// Hands the review folder over to the airplay folder instead of copying it — the usual case,
    /// since whole albums are approved far more often than single tracks are dropped.
    /// <para>
    /// Inside the Nextcloud root a move is a rename the sync client replays server-side rather
    /// than a second upload of the same 100+ MB, it is instant instead of file-by-file, and it
    /// works on dehydrated placeholders, which <c>File.Copy</c> does not.
    /// </para>
    /// <para>
    /// Returns false whenever the move is not obviously safe — an existing destination (which the
    /// copy path merges into track by track), a different volume, or an open handle on a track.
    /// The caller then falls back to copying, so a "no" costs nothing but time.
    /// </para>
    /// </summary>
    private static bool TryMove(string reviewFolder, string destination)
    {
        if (!Directory.Exists(reviewFolder) || Directory.Exists(destination))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(reviewFolder, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;   // nothing has moved: Directory.Move does not copy across volumes
        }

        // The sidecar travels with the folder, and the airplay folder is what gets broadcast.
        var sidecar = Path.Combine(destination, ReviewState.FileName);
        try
        {
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A hidden leftover json is not worth failing a finished publish over.
        }

        return true;
    }
}
