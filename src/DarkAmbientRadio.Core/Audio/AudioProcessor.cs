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
    /// How many files are worked on at once. ffmpeg's libmp3lame is single-threaded per file, so
    /// several tracks really do use several cores. <c>ProcessorCount / 2</c> because the count
    /// includes SMT threads, from which LAME gains almost nothing — it already keeps the FPU busy.
    /// <para>
    /// Measured on an 11-track album, Ryzen 7 4800U (8C/16T, 15 W), encode phase only:
    /// 1 → 64 s, 2 → 39 s, 4 → 25 s, 8 → 21 s, 16 → 20 s. The knee is the physical core count;
    /// past it the package power limit throttles the clocks and the last 8 threads buy 5 %.
    /// Hence the cap at 8: on a bigger machine the extra parallelism would not pay for the
    /// concurrent writes into the cloud-synced review folder either.
    /// </para>
    /// </summary>
    private static int MaxParallelTracks => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    /// <summary>
    /// How many mp3gain processes run at once — higher than <see cref="MaxParallelTracks"/>,
    /// because gain analysis keeps scaling past the physical core count where LAME has stopped.
    /// Same album and machine, normalisation phase: 1 → 32 s, 2 → 18 s, 4 → 11 s, 8 → 8 s,
    /// 16 → 6.5 s. The processes are short-lived and spend a good share of their time reading the
    /// file rather than computing, so unlike LAME they do find work for the SMT threads.
    /// </summary>
    private static int MaxParallelNormalizations => Math.Clamp(Environment.ProcessorCount, 1, 16);

    /// <summary>
    /// Processes every file in <paramref name="sourceFolder"/> into a same-named folder
    /// under <paramref name="reviewDir"/> and returns that destination path.
    /// <para>
    /// Tracks are handled in parallel (<see cref="MaxParallelTracks"/>): the per-file work is an
    /// ffmpeg process plus, occasionally, a cloud fetch that is pure waiting. Normalisation still
    /// happens in one mp3gain call over the whole album afterwards — the album is the unit there.
    /// </para>
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

        var files = Directory.EnumerateFiles(sourceFolder)
            .Where(f => !Path.GetFileName(f).Equals(Models.ReviewState.FileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Index-aligned so the mp3gain argument list stays in album order no matter which track
        // finishes first — no lock needed, every slot has exactly one writer.
        var encodedMp3s = new string?[files.Count];
        var trackCount = files.Count(IsMp3);
        var done = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelTracks,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), options, async (i, token) =>
        {
            var file = files[i];
            var name = Path.GetFileName(file);
            var target = Path.Combine(destination, name);

            // Nextcloud may hold this file as a placeholder; ffmpeg and File.Copy both fail on
            // one whose on-demand fetch stumbles, so pull it down first and retry.
            if (CloudFiles.IsPlaceholder(file))
                progress?.Report($"Lade {name} aus der Cloud …");
            CloudFiles.Hydrate(file, ct: token);

            if (!IsMp3(file))
            {
                CloudFiles.Copy(file, target, token);
                return;
            }

            if (reencode)
                await EncodeAsync(file, target, token);
            else
                CloudFiles.Copy(file, target, token);

            encodedMp3s[i] = target;
            var finished = Interlocked.Increment(ref done);
            progress?.Report(reencode
                ? $"Recode {finished}/{trackCount}: {name}"
                : $"Übernehme {finished}/{trackCount}: {name}");
        });

        var normalizable = encodedMp3s.Where(p => p is not null).Select(p => p!).ToList();
        if (normalizable.Count > 0)
        {
            progress?.Report($"Normalisierung ({normalizable.Count} Tracks) …");
            await NormalizeAsync(normalizable, progress, ct);
        }

        return destination;
    }

    private static bool IsMp3(string path)
        => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Applies track gain to every file, one mp3gain process per track and several at a time.
    /// <para>
    /// Handing mp3gain the whole album in one call would be the obvious thing, but that call is
    /// single-threaded and analysing a track is exactly as CPU-bound as encoding it — it decodes
    /// the whole file. One process per track is equivalent in result because the applied gain is
    /// per file: <c>/r</c> is <em>track</em> gain (album gain would be <c>/a</c>, which really does
    /// need to see every track at once) and <c>/k</c> caps clipping per file too. Per track also
    /// balances itself, which chunking the list into equal parts would not — track lengths differ.
    /// </para>
    /// </summary>
    private async Task NormalizeAsync(
        IReadOnlyList<string> mp3Files,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var done = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelNormalizations,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(mp3Files, options, async (file, token) =>
        {
            await Cli.Wrap(_mp3gainPath)
                .WithArguments(NormalizeArguments(file))
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteAsync(token);

            var finished = Interlocked.Increment(ref done);
            progress?.Report($"Normalisierung {finished}/{mp3Files.Count} …");
        });
    }

    private IReadOnlyList<string> NormalizeArguments(string mp3File) =>
        // /r: apply Track gain; /d: shift target from the 89 dB reference; /k: auto-lower to
        // prevent clipping (also avoids the interactive clip prompt); /p: preserve timestamp;
        // /q: quiet. mp3gain documents switches with '/'; the negative delta is a separate arg.
        [
            "/r", "/k", "/p", "/q",
            "/d", _mp3gainDelta.ToString("0.0", CultureInfo.InvariantCulture),
            mp3File,
        ];
}
