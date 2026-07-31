using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DarkAmbientRadio.App.Services;
using DarkAmbientRadio.App.Views;
using DarkAmbientRadio.Core.Airplay;
using DarkAmbientRadio.Core.Config;
using DarkAmbientRadio.Core.Library;
using DarkAmbientRadio.Core.Review;
using DarkAmbientRadio.Core.Sources;

namespace DarkAmbientRadio.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ReviewStore _store = new();
    private readonly AlbumLibrary _library = new();
    private readonly ClipboardCodeSource _codeSource;

    private TaskCompletionSource? _loginGate;
    private int _currentTrackIndex;

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

    public ObservableCollection<AlbumViewModel> Albums { get; } = new();

    [ObservableProperty]
    private AlbumViewModel? _selectedAlbum;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showContinueButton;

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
            PublishCommand.NotifyCanExecuteChanged();
            return;
        }

        newValue.PropertyChanged += OnSelectedAlbumPropertyChanged;
        PublishCommand.NotifyCanExecuteChanged();

        // Auto-play from the first track.
        _currentTrackIndex = 0;
        PlayCurrentTrack();
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
            await Task.Run(() => workflow.RunAsync(code, WaitForManualLoginAsync, progress, CancellationToken.None));
            LoadAlbums();
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

    /// <summary>Imports one or more downloaded ZIPs (drag-and-drop onto the album list).</summary>
    public async Task ImportZipsAsync(IReadOnlyList<string> zipPaths)
    {
        if (IsBusy)
            return;

        var zips = zipPaths
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .ToList();
        if (zips.Count == 0)
        {
            StatusText = "Keine .zip-Dateien zum Importieren.";
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(msg => StatusText = msg);
        try
        {
            var workflow = new AcquisitionWorkflow(_config);
            foreach (var zip in zips)
                await Task.Run(() => workflow.ProcessZipAsync(zip, progress, CancellationToken.None));
            LoadAlbums();
            StatusText = zips.Count == 1 ? "Import fertig." : $"{zips.Count} Alben importiert.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import-Fehler: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
