using System.Globalization;
using CliWrap;
using DarkAmbientRadio.Core.Files;

namespace DarkAmbientRadio.Core.Audio;

/// <summary>
/// Re-encodes an album's MP3s to the target bitrate (ffmpeg) and applies track-gain
/// normalisation to the configured loudness (mp3gain), writing into the review folder.
/// Non-audio assets (cover art, images) are copied over untouched.
/// </summary>
public sealed class AudioProcessor
{
    private readonly string _ffmpegPath;
    private readonly string _mp3gainPath;
    private readonly string _bitrate;
    private readonly double _mp3gainDelta;

    public AudioProcessor(string? ffmpegPath, string? mp3gainPath, string bitrate, double mp3gainDelta)
    {
        _ffmpegPath = ToolLocator.Resolve(ffmpegPath, "ffmpeg.exe");
        _mp3gainPath = ToolLocator.Resolve(mp3gainPath, "mp3gain.exe");
        _bitrate = bitrate;
        _mp3gainDelta = mp3gainDelta;
    }

    /// <summary>
    /// Processes every file in <paramref name="sourceFolder"/> into a same-named folder
    /// under <paramref name="reviewDir"/> and returns that destination path.
    /// </summary>
    /// <param name="reencode">
    /// False takes the MP3s over unchanged instead of running them through ffmpeg — for material
    /// that already is exactly what this would produce, where a second lossy pass only costs
    /// quality. Normalisation runs either way.
    /// </param>
    public async Task<string> ProcessAlbumAsync(
        string sourceFolder,
        string reviewDir,
        bool reencode = true,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var destination = Path.Combine(reviewDir, Path.GetFileName(sourceFolder));
        Directory.CreateDirectory(destination);

        var encodedMp3s = new List<string>();

        foreach (var file in Directory.EnumerateFiles(sourceFolder))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            var target = Path.Combine(destination, name);

            if (name.Equals(Models.ReviewState.FileName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Nextcloud may hold this file as a placeholder; ffmpeg and File.Copy both fail on
            // one whose on-demand fetch stumbles, so pull it down first and retry.
            if (CloudFiles.IsPlaceholder(file))
                progress?.Report($"Lade {name} aus der Cloud …");
            CloudFiles.Hydrate(file, ct: ct);

            if (Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                if (reencode)
                {
                    progress?.Report($"Recode: {name}");
                    await EncodeAsync(file, target, ct);
                }
                else
                {
                    progress?.Report($"Übernehme: {name}");
                    CloudFiles.Copy(file, target, ct);
                }

                encodedMp3s.Add(target);
            }
            else
            {
                CloudFiles.Copy(file, target, ct);
            }
        }

        if (encodedMp3s.Count > 0)
        {
            progress?.Report($"Normalisierung ({encodedMp3s.Count} Tracks) …");
            await NormalizeAsync(encodedMp3s, ct);
        }

        return destination;
    }

    private async Task EncodeAsync(string source, string target, CancellationToken ct)
    {
        await Cli.Wrap(_ffmpegPath)
            .WithArguments(new[]
            {
                "-y",
                "-i", source,
                "-map_metadata", "0",
                "-id3v2_version", "3",
                "-codec:a", "libmp3lame",
                "-b:a", _bitrate,
                target,
            })
            .WithValidation(CommandResultValidation.ZeroExitCode)
            .ExecuteAsync(ct);
    }

    private async Task NormalizeAsync(IReadOnlyList<string> mp3Files, CancellationToken ct)
    {
        // /r: apply Track gain; /d: shift target from the 89 dB reference; /k: auto-lower to
        // prevent clipping (also avoids the interactive clip prompt); /p: preserve timestamp;
        // /q: quiet. mp3gain documents switches with '/'; the negative delta is a separate arg.
        var args = new List<string>
        {
            "/r", "/k", "/p", "/q",
            "/d", _mp3gainDelta.ToString("0.0", CultureInfo.InvariantCulture),
        };
        args.AddRange(mp3Files);

        await Cli.Wrap(_mp3gainPath)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.ZeroExitCode)
            .ExecuteAsync(ct);
    }
}
