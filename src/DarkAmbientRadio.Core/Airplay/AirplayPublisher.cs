using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Airplay;

public sealed record PublishResult(string DestinationFolder, string FolderName, int TrackCount);

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
    /// </summary>
    public PublishResult Publish(Album album, string airplayDir)
    {
        var approved = album.ApprovedTracks.ToList();
        if (approved.Count == 0)
            throw new InvalidOperationException("Keine freigegebenen Tracks – nichts zum Ausstrahlen.");

        var folderName = BuildFolderName(album);
        var destination = Path.Combine(airplayDir, folderName);
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

        return new PublishResult(destination, folderName, approved.Count);
    }
}
