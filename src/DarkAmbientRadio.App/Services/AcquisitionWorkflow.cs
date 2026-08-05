using System.IO;
using DarkAmbientRadio.Core.Audio;
using DarkAmbientRadio.Core.Bandcamp;
using DarkAmbientRadio.Core.Config;
using DarkAmbientRadio.Core.Files;

namespace DarkAmbientRadio.App.Services;

/// <summary>What one import produced.</summary>
/// <param name="ReviewFolder">The review copy, i.e. the album to select afterwards.</param>
/// <param name="SourceToRemove">
/// The dropped folder left behind by a cross-volume copy, which the caller must ask about;
/// null when there is nothing to clean up.
/// </param>
/// <param name="LowBitrateTracks">
/// The <em>source</em> tracks below <see cref="AcquisitionWorkflow.LowBitrateThresholdKbps"/>.
/// Empty is the normal case; anything in here means the master was already poor and the review
/// copy only looks like 192 kbit/s.
/// </param>
public readonly record struct AlbumImportResult(
    string ReviewFolder,
    string? SourceToRemove,
    IReadOnlyList<TrackBitrate> LowBitrateTracks);

/// <summary>
/// Orchestrates the acquisition half of the workflow: redeem a code on Bandcamp,
/// download the ZIP, unpack into the archive, then re-encode + normalise into the
/// review folder.
/// </summary>
public sealed class AcquisitionWorkflow
{
    /// <summary>
    /// Below this the source material is worth complaining about: re-encoding lifts every track
    /// to the target bitrate, so nothing downstream can still tell that it came from 128 kbit/s.
    /// </summary>
    public const int LowBitrateThresholdKbps = 160;

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

        return (await ProcessZipAsync(zipPath, progress, ct)).ReviewFolder;
    }

    /// <summary>
    /// Processes an already-downloaded ZIP (unpack -> re-encode -> normalise) into the review
    /// folder. Used both by the Bandcamp flow and by drag-and-drop import.
    /// </summary>
    public async Task<AlbumImportResult> ProcessZipAsync(
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
    public async Task<AlbumImportResult> ProcessFolderAsync(
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

        var imported = await ProcessAlbumFolderAsync(archived.TargetPath, reencode, progress, ct);
        return imported with { SourceToRemove = archived.SourceToRemove };
    }

    /// <summary>Re-encodes and normalises an archived album folder into the review folder.</summary>
    private async Task<AlbumImportResult> ProcessAlbumFolderAsync(
        string archiveFolder,
        bool reencode,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var audio = new AudioProcessor(_config.FfmpegPath, _config.Mp3gainPath, _config.Bitrate, _config.Mp3GainDelta);
        var reviewFolder = await audio.ProcessAlbumAsync(archiveFolder, _config.ReviewDir, reencode, progress, ct);

        // The master, not the review copy: the encode lifts everything to the target bitrate, so
        // only the source can still say that the material was poor. Probing afterwards also means
        // the pipeline has already pulled every track out of the cloud.
        var lowBitrate = await Task.Run(
            () => TrackMetadata.FindTracksBelow(archiveFolder, LowBitrateThresholdKbps), ct);

        progress.Report("Fertig – Album ist im Review.");
        return new AlbumImportResult(reviewFolder, SourceToRemove: null, lowBitrate);
    }
}
