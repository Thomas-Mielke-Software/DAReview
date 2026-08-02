using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DarkAmbientRadio.App.Services;
using DarkAmbientRadio.App.Views;
using DarkAmbientRadio.Core.Airplay;
using DarkAmbientRadio.Core.Audio;
using DarkAmbientRadio.Core.Config;
using DarkAmbientRadio.Core.Files;
using DarkAmbientRadio.Core.Library;
using DarkAmbientRadio.Core.Naming;
using DarkAmbientRadio.Core.Review;
using DarkAmbientRadio.Core.Sources;

namespace DarkAmbientRadio.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ReviewStore _store = new();
    private readonly AlbumLibrary _library = new();
    private readonly ClipboardCodeSource _codeSource;

    /// <summary>Concurrent Nextcloud hydration requests — enough to pipeline, not enough to thrash.</summary>
    private const int PrefetchParallelism = 4;

    private TaskCompletionSource? _loginGate;
    private CancellationTokenSource? _prefetchCts;
    private int _currentTrackIndex;
    private int _trackInfoToken;

    /// <summary>Raised with a file path when the view should start playing a track.</summary>
    public event Action<string>? PlayFileRequested;

    /// <summary>Raised when the view should stop playback.</summary>
    public event Action? StopRequested;

    public MainViewModel(AppConfig config)
    {
        _config = config;
        _codeSource = new ClipboardCodeSource(new ReviewCodeValidator(config.CodeRegex));
        LoadAlbums();
    }

    /// <summary>The live config instance; the view persists its window placement into it.</summary>
    public AppConfig Config => _config;

    public ObservableCollection<AlbumViewModel> Albums { get; } = new();

    [ObservableProperty]
    private AlbumViewModel? _selectedAlbum;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value) => NotifyAlbumCommandsChanged();

    [ObservableProperty]
    private bool _showContinueButton;

    /// <summary>ID3/stream facts of the playing track, shown between player and track list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrackInfo))]
    private TrackInfoViewModel? _trackInfo;

    public bool HasTrackInfo => TrackInfo is not null;

    // ----- Album loading -----------------------------------------------------

    private void LoadAlbums()
    {
        Albums.Clear();
        foreach (var album in _library.LoadReviewQueue(_config.ReviewDir))
            Albums.Add(new AlbumViewModel(album, _store));
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadAlbums();
        StatusText = $"{Albums.Count} Alben im Review.";
    }

    /// <summary>
    /// Picks a random album and selects it (which auto-plays its first track). Called by the
    /// view once it has subscribed to playback events, so the audio actually starts.
    /// </summary>
    public void SelectRandomAlbum()
    {
        if (Albums.Count == 0)
            return;
        SelectedAlbum = Albums[Random.Shared.Next(Albums.Count)];
    }

    /// <summary>
    /// Selects the album that lives in <paramref name="folderPath"/>, which auto-plays it.
    /// Used after an import so the freshly acquired album starts playing. Must be called
    /// after <see cref="LoadAlbums"/>, whose rebuilt view models are what gets matched.
    /// </summary>
    private void SelectAlbumByFolder(string folderPath)
    {
        var target = Normalise(folderPath);
        var match = Albums.FirstOrDefault(
            a => string.Equals(Normalise(a.Album.FolderPath), target, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            SelectedAlbum = match;

        static string Normalise(string path)
            => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    // ----- Selection & playback ---------------------------------------------

    partial void OnSelectedAlbumChanged(AlbumViewModel? oldValue, AlbumViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnSelectedAlbumPropertyChanged;

        ClearPlayingFlags(oldValue);

        if (newValue is null)
        {
            // Only fully stop when nothing is selected; when switching albums the new
            // track's Source replaces the old one (avoids a stop/play race that could
            // swallow the auto-play of the first track).
            StopRequested?.Invoke();
            ClearTrackInfo();
            NotifyAlbumCommandsChanged();
            return;
        }

        newValue.PropertyChanged += OnSelectedAlbumPropertyChanged;
        NotifyAlbumCommandsChanged();

        PrefetchTracks(newValue);

        // Auto-play from the first track.
        _currentTrackIndex = 0;
        PlayCurrentTrack();
    }

    /// <summary>
    /// Touches the first byte of every track so Nextcloud hydrates the whole album in one go
    /// instead of stalling at each track change. Fire-and-forget and deliberately parallel;
    /// failures are irrelevant here — playback reports its own errors.
    /// </summary>
    private void CancelPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = null;
    }

    private void PrefetchTracks(AlbumViewModel album)
    {
        CancelPrefetch();
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;

        var paths = album.Tracks.Select(t => t.FilePath).ToArray();
        if (paths.Length == 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(
                    paths,
                    new ParallelOptions { MaxDegreeOfParallelism = PrefetchParallelism, CancellationToken = cts.Token },
                    static async (path, token) =>
                    {
                        // ReadWrite: the player may already hold this very file open.
                        // Delete: without it an in-flight prefetch blocks renames, which is
                        // exactly what the normalisation buttons do to these files.
                        await using var stream = new FileStream(
                            path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            bufferSize: 1, useAsync: true);
                        // ReadExactly, not Read: we want the byte to actually arrive, which is
                        // what forces Nextcloud to hydrate the placeholder.
                        var probe = new byte[1];
                        await stream.ReadExactlyAsync(probe, token);
                    });
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException
                                          or UnauthorizedAccessException)
            {
                // Album switched away, or a file vanished/is locked — nothing to do.
            }
        }, cts.Token);
    }

    private void OnSelectedAlbumPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlbumViewModel.CanPublish))
            PublishCommand.NotifyCanExecuteChanged();
    }

    private void PlayCurrentTrack()
    {
        var album = SelectedAlbum;
        if (album is null || _currentTrackIndex < 0 || _currentTrackIndex >= album.Tracks.Count)
            return;

        ClearPlayingFlags(album);
        var track = album.Tracks[_currentTrackIndex];
        track.IsPlaying = true;
        PlayFileRequested?.Invoke(track.FilePath);
        LoadTrackInfo(track.FilePath);
    }

    /// <summary>
    /// Reads the tags off the UI thread — on a cold Nextcloud file that read can block for a
    /// moment, and it must not delay playback. A stale result (the user moved on meanwhile)
    /// is dropped via the token.
    /// </summary>
    private async void LoadTrackInfo(string filePath)
    {
        var token = ++_trackInfoToken;
        TrackInfo = null;

        var metadata = await Task.Run(() => TrackMetadata.Read(filePath));
        if (token == _trackInfoToken)
            TrackInfo = new TrackInfoViewModel(metadata, filePath, ExpectedBitrate);
    }

    private void ClearTrackInfo()
    {
        _trackInfoToken++;   // invalidates a read that is still running
        TrackInfo = null;
    }

    /// <summary>The configured target bitrate in kbit/s (<c>"192k"</c> → 192), 0 if unparsable.</summary>
    private int ExpectedBitrate
    {
        get
        {
            var digits = new string(_config.Bitrate?.TakeWhile(char.IsAsciiDigit).ToArray() ?? []);
            if (!int.TryParse(digits, out var value) || value <= 0)
                return 0;
            return value >= 1000 ? value / 1000 : value;   // accept "192000" as well as "192k"
        }
    }

    /// <summary>Called by the view when the current track finishes.</summary>
    public void OnTrackEnded()
    {
        var album = SelectedAlbum;
        if (album is null)
            return;

        album.RecordTrackPlayed();

        _currentTrackIndex++;
        if (_currentTrackIndex < album.Tracks.Count)
        {
            PlayCurrentTrack();
        }
        else
        {
            ClearPlayingFlags(album);
            StopRequested?.Invoke();
            ClearTrackInfo();
            StatusText = $"Album durchgehört ({album.ListenPercentText}).";
        }
    }

    /// <summary>Called by the view when the user clicks a track to play it directly.</summary>
    public void PlayTrack(TrackViewModel track)
    {
        var album = SelectedAlbum;
        if (album is null)
            return;
        _currentTrackIndex = album.Tracks.IndexOf(track);
        PlayCurrentTrack();
    }

    /// <summary>Play/resume the current track (used by the transport bar when idle).</summary>
    public void PlayCurrentOrFirst()
    {
        if (SelectedAlbum is null || SelectedAlbum.Tracks.Count == 0)
            return;
        if (_currentTrackIndex < 0 || _currentTrackIndex >= SelectedAlbum.Tracks.Count)
            _currentTrackIndex = 0;
        PlayCurrentTrack();
    }

    private static void ClearPlayingFlags(AlbumViewModel? album)
    {
        if (album is null)
            return;
        foreach (var t in album.Tracks)
            t.IsPlaying = false;
    }

    // ----- Acquisition -------------------------------------------------------

    [RelayCommand]
    private async Task AcquireAsync()
    {
        if (IsBusy)
            return;

        var code = _codeSource.TryGetCode();
        if (code is null)
        {
            StatusText = "Kein gültiger Review-Code in der Zwischenablage.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(msg => StatusText = msg);
        try
        {
            var workflow = new AcquisitionWorkflow(_config);
            var reviewFolder = await Task.Run(
                () => workflow.RunAsync(code, WaitForManualLoginAsync, progress, CancellationToken.None));
            LoadAlbums();
            SelectAlbumByFolder(reviewFolder);   // start reviewing the new album right away
        }
        catch (Exception ex)
        {
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ShowContinueButton = false;
        }
    }

    /// <summary>
    /// Imports what was dropped onto the album list and starts playing the result (the first
    /// item, if several were dropped at once). Both ZIPs and album folders run through the
    /// full pipeline (archive → re-encode → normalise → review), so nothing reaches the
    /// review queue at the wrong bitrate or without normalisation.
    /// </summary>
    public async Task ImportDroppedAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy)
            return;

        var zips = paths
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .ToList();
        var folders = paths.Where(Directory.Exists).ToList();

        if (zips.Count == 0 && folders.Count == 0)
        {
            StatusText = "Nichts zum Importieren – .zip-Datei oder Album-Ordner ablegen.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(msg => StatusText = msg);
        var imported = new List<string>();
        var failures = new List<string>();
        try
        {
            var workflow = new AcquisitionWorkflow(_config);

            // Each item stands on its own: one album that fails must not take the rest of the
            // drop with it (a single stumbling cloud fetch used to abort the whole batch).
            foreach (var zip in zips)
            {
                try
                {
                    imported.Add(await Task.Run(() => workflow.ProcessZipAsync(zip, progress, CancellationToken.None)));
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(zip)}: {ex.Message}");
                }
            }

            foreach (var folder in folders)
            {
                try
                {
                    var reencode = await ConfirmReencodeAsync(folder);
                    var result = await Task.Run(
                        () => workflow.ProcessFolderAsync(folder, reencode, progress, CancellationToken.None));
                    imported.Add(result.TargetPath);

                    if (result.SourceToRemove is { } leftover)
                        await ConfirmAndDeleteSourceAsync(leftover);
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}: {ex.Message}");
                }
            }

            LoadAlbums();
            if (imported.Count > 0)
                SelectAlbumByFolder(imported[0]);   // start reviewing the new album right away

            ReportImportOutcome(imported.Count, failures);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportImportOutcome(int importedCount, IReadOnlyList<string> failures)
    {
        if (failures.Count == 0)
        {
            StatusText = importedCount == 1 ? "Import fertig." : $"{importedCount} Alben importiert.";
            return;
        }

        StatusText = importedCount > 0
            ? $"{importedCount} importiert, {failures.Count} fehlgeschlagen."
            : $"Import fehlgeschlagen ({failures.Count}).";

        // The status line truncates and these are the details that matter for a retry.
        MessageBox.Show(
            string.Join("\n\n", failures),
            failures.Count == 1 ? "Import fehlgeschlagen" : $"{failures.Count} Importe fehlgeschlagen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Asks before re-encoding an album that already is exactly what the pipeline would produce.
    /// MP3 is lossy, so a second pass costs quality for nothing. The check reads the MPEG frame
    /// headers rather than an average bitrate — a VBR file can average the target exactly and
    /// still be a different animal; anything not provably CBR at the target is re-encoded.
    /// </summary>
    private async Task<bool> ConfirmReencodeAsync(string folder)
    {
        var target = ExpectedBitrate;
        if (target <= 0)
            return true;

        StatusText = $"Prüfe Bitrate von {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))} …";
        var info = await Task.Run(() => Mp3StreamProbe.ProbeAlbum(folder));
        if (!info.IsConstantAt(target))
            return true;

        var answer = MessageBox.Show(
            $"»{Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}« liegt bereits "
            + $"vollständig in {target} kbit/s CBR vor ({info.TrackCount} Tracks).\n\n"
            + "Ein erneutes Encodieren würde die Qualität verschlechtern, ohne etwas zu ändern.\n\n"
            + "Recode überspringen und die Dateien unverändert übernehmen?\n\n"
            + $"Die Normalisierung auf {_config.NormalizationDb:0.#} dB läuft in beiden Fällen.",
            "Album ist bereits im Zielformat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);

        return answer != MessageBoxResult.Yes;
    }

    /// <summary>
    /// Asks before removing the original after a cross-volume copy. Defaults to "no": the album
    /// is safely in the review queue either way, so keeping the source costs nothing but space.
    /// </summary>
    private async Task ConfirmAndDeleteSourceAsync(string source)
    {
        var answer = MessageBox.Show(
            $"»{Path.GetFileName(source)}« liegt auf einem anderen Laufwerk und wurde deshalb "
            + "kopiert statt verschoben.\n\nSoll der Ursprungsordner jetzt gelöscht werden?\n\n"
            + source,
            "Ursprungsordner löschen?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            StatusText = "Kopiert – Ursprungsordner behalten.";
            return;
        }

        await Task.Run(() => Directory.Delete(source, recursive: true));
        StatusText = "Kopiert – Ursprungsordner gelöscht.";
    }

    private Task WaitForManualLoginAsync(CancellationToken ct)
    {
        _loginGate = new TaskCompletionSource();
        ct.Register(() => _loginGate.TrySetCanceled());
        Application.Current.Dispatcher.Invoke(() => ShowContinueButton = true);
        return _loginGate.Task;
    }

    [RelayCommand]
    private void ContinueLogin()
    {
        ShowContinueButton = false;
        _loginGate?.TrySetResult();
    }

    // ----- Publish -----------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private void Publish()
    {
        var album = SelectedAlbum;
        if (album is null)
            return;

        try
        {
            var publisher = new AirplayPublisher(_config.ConnectorOhne, _config.ConnectorNur);
            var result = publisher.Publish(album.Album, _config.AirplayDir);
            _store.MarkPublished(album.Album, result.FolderName);

            StopRequested?.Invoke();
            Albums.Remove(album);
            SelectedAlbum = null;
            StatusText = $"Airplay: {result.FolderName} ({result.TrackCount} Tracks).";
        }
        catch (Exception ex)
        {
            StatusText = $"Airplay fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanPublish() => SelectedAlbum?.CanPublish == true;

    private void NotifyAlbumCommandsChanged()
    {
        PublishCommand.NotifyCanExecuteChanged();
        RejectAlbumCommand.NotifyCanExecuteChanged();
        NormalizeAlbumTitleCommand.NotifyCanExecuteChanged();
        NormalizeArtistCommand.NotifyCanExecuteChanged();
    }

    // ----- Reject ------------------------------------------------------------

    /// <summary>
    /// The counterpart to publishing: throw the album out of the review queue by deleting its
    /// folder, optionally together with the archived 320k master.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedAlbum))]
    private void RejectAlbum()
    {
        var album = SelectedAlbum;
        if (album is null)
            return;

        var reviewFolder = album.Album.FolderPath;
        var archiveFolder = AlbumRemover.FindArchiveFolder(reviewFolder, _config.ArchiveDir);

        var dialog = new DeleteAlbumWindow(album.Name, reviewFolder, archiveFolder)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true)
            return;

        var alsoArchive = dialog.AlsoDeleteArchive && archiveFolder is not null;

        // Every open handle has to go first — the player holds the current track and a
        // Nextcloud prefetch may still be streaming the rest.
        StopRequested?.Invoke();
        ClearTrackInfo();
        CancelPrefetch();

        try
        {
            var remover = new AlbumRemover();
            RetryWhileLocked(() => remover.Delete(reviewFolder));
            if (alsoArchive)
                remover.Delete(archiveFolder!);

            Albums.Remove(album);
            SelectedAlbum = null;
            StatusText = alsoArchive
                ? $"Abgelehnt: {album.Name} – Review und Archiv im Papierkorb."
                : $"Abgelehnt: {album.Name} – Review-Ordner im Papierkorb.";
        }
        catch (Exception ex)
        {
            StatusText = $"Löschen fehlgeschlagen: {ex.Message}";
        }
    }

    // ----- Normalisation -----------------------------------------------------

    private bool HasSelectedAlbum() => SelectedAlbum is not null && !IsBusy;

    /// <summary>Title-cases the album title in the folder name, track filenames and ID3 tags.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedAlbum))]
    private Task NormalizeAlbumTitleAsync()
        => NormalizeAsync((normalizer, path) => normalizer.NormalizeAlbumTitle(path), "Album-Titel");

    /// <summary>Title-cases the artist in the folder name, track filenames and ID3 tags.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedAlbum))]
    private Task NormalizeArtistAsync()
        => NormalizeAsync((normalizer, path) => normalizer.NormalizeArtist(path), "Artist");

    private async Task NormalizeAsync(Func<AlbumNormalizer, string, NormalizeResult> apply, string what)
    {
        var album = SelectedAlbum;
        if (album is null || IsBusy)
            return;

        var folderPath = album.Album.FolderPath;

        // Every open handle on these files has to go before renaming: the player holds the
        // current track, and a Nextcloud prefetch may still be streaming several more.
        StopRequested?.Invoke();
        ClearTrackInfo();
        CancelPrefetch();

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => RetryWhileLocked(() => apply(new AlbumNormalizer(), folderPath)));
            LoadAlbums();
            SelectAlbumByFolder(result.FolderPath);
            StatusText = result.AnyChange
                ? $"{what} normalisiert – {result.RenamedFiles} Datei(en) umbenannt, "
                  + $"{result.RetaggedFiles} Tag(s) aktualisiert"
                  + (result.FolderRenamed ? ", Ordner umbenannt." : ".")
                : $"{what}: Schreibweise war bereits normalisiert.";
        }
        catch (Exception ex)
        {
            StatusText = $"Normalisierung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Releasing the media file handle after <see cref="StopRequested"/> is not instant, so a
    /// first rename attempt can still hit a lock. Retrying is safe because normalisation is
    /// idempotent — already-correct names and tags are simply left untouched.
    /// </summary>
    private static void RetryWhileLocked(Action action, int attempts = 5)
        => RetryWhileLocked<object?>(() => { action(); return null; }, attempts);

    private static T RetryWhileLocked<T>(Func<T> action, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(200);
            }
        }
    }

    // ----- Settings ----------------------------------------------------------

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow(_config) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            _config.Save();
            LoadAlbums();
            StatusText = "Einstellungen gespeichert.";
        }
    }
}
