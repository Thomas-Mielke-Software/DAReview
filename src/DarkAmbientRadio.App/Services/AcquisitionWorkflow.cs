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

        var audio = new AudioProcessor(_config.FfmpegPath, _config.Mp3gainPath, _config.Bitrate, _config.Mp3GainDelta);
        var reviewFolder = await audio.ProcessAlbumAsync(archiveFolder, _config.ReviewDir, progress, ct);

        progress.Report("Fertig – Album ist im Review.");
        return reviewFolder;
    }
}
