using System.IO;
using DarkAmbientRadio.Core.Audio;
using DarkAmbientRadio.Core.Bandcamp;
using DarkAmbientRadio.Core.Config;
using DarkAmbientRadio.Core.Files;

namespace DarkAmbientRadio.App.Services;

/// <summary>
/// Orchestrates the acquisition half of the workflow: redeem a code on Bandcamp,
/// download the ZIP, unpack into the archive, then re-encode + normalise into the
/// review folder.
/// </summary>
public sealed class AcquisitionWorkflow
{
    private readonly AppConfig _config;

    public AcquisitionWorkflow(AppConfig config) => _config = config;

    /// <summary>Runs the pipeline and returns the created review folder path.</summary>
    public async Task<string> RunAsync(
        string code,
        Func<CancellationToken, Task> waitForManualLogin,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        var browserDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DarkAmbientRadio", "browser");

        string zipPath;
        await using (var redeemer = new BandcampRedeemer(browserDir, _config.DownloadDir))
        {
            zipPath = await redeemer.RedeemAsync(code, addToCollection: true, waitForManualLogin, progress, ct);
        }

        return await ProcessZipAsync(zipPath, progress, ct);
    }

    /// <summary>
    /// Processes an already-downloaded ZIP (unpack -> re-encode -> normalise) into the review
    /// folder. Used both by the Bandcamp flow and by drag-and-drop import.
    /// </summary>
    public async Task<string> ProcessZipAsync(
        string zipPath,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        progress.Report($"Entpacke {Path.GetFileName(zipPath)} …");
        var archiveFolder = new ArchiveExtractor().Extract(zipPath, _config.ArchiveDir);

        return await ProcessAlbumFolderAsync(archiveFolder, reencode: true, progress, ct);
    }

    /// <summary>
    /// Processes an album that arrives as a plain folder (drag and drop). It first takes the
    /// place a ZIP's contents would have taken — the archive, as the untouched master — and is
    /// then re-encoded and normalised into the review folder like everything else. Returns
    /// the review folder plus, after a cross-volume copy, the source the caller must ask about.
    /// </summary>
    /// <param name="reencode">
    /// False when the caller established that the folder already holds exactly what the encode
    /// would produce (see <see cref="Mp3StreamProbe"/>); the MP3s are then taken over as they are.
    /// </param>
    public async Task<FolderImportResult> ProcessFolderAsync(
        string folderPath,
        bool reencode,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath)));

        // Moving a review album back out through the pipeline would leave it half-archived if
        // the re-encode failed; refuse instead of guessing what was meant.
        if (FolderImporter.IsAtOrUnder(_config.ReviewDir, folderPath))
            throw new IOException($"»{name}« liegt bereits im Review-Ordner.");

        // Pull cloud placeholders down while the file still sits at the path the sync client
        // knows. Moving a placeholder to a new path inside the sync root means the provider must
        // replay that move on the server before it can serve the content at all, so hydrating
        // afterwards races the sync — which is exactly how covers used to fail the import.
        CloudFiles.HydrateFolder(folderPath, progress, ct);

        // An album that is already archived stays put — only the review copy gets rebuilt.
        var archived = FolderImporter.IsAtOrUnder(_config.ArchiveDir, folderPath)
            ? new FolderImportResult(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath)), null)
            : new FolderImporter().Import(folderPath, _config.ArchiveDir, progress, ct);

        var reviewFolder = await ProcessAlbumFolderAsync(archived.TargetPath, reencode, progress, ct);
        return archived with { TargetPath = reviewFolder };
    }

    /// <summary>Re-encodes and normalises an archived album folder into the review folder.</summary>
    private async Task<string> ProcessAlbumFolderAsync(
        string archiveFolder,
        bool reencode,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var audio = new AudioProcessor(_config.FfmpegPath, _config.Mp3gainPath, _config.Bitrate, _config.Mp3GainDelta);
        var reviewFolder = await audio.ProcessAlbumAsync(archiveFolder, _config.ReviewDir, reencode, progress, ct);

        progress.Report("Fertig – Album ist im Review.");
        return reviewFolder;
    }
}
