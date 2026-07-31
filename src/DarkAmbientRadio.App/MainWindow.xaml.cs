using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DarkAmbientRadio.App.ViewModels;

namespace DarkAmbientRadio.App;

/// <summary>
/// Interaction logic for MainWindow.xaml. Bridges the MVVM view model to the
/// code-controlled <c>MediaElement</c> and drives the transport bar (play/pause,
/// seekable position slider).
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxStartAttempts = 10;

    private MainViewModel? _viewModel;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _startWatchdog;
    private Uri? _pendingSource;    // source the watchdog re-loads if Play() was dropped
    private int _startAttempts;
    private bool _updatingSlider;   // guards programmatic slider updates from re-seeking
    private bool _hasMedia;

    public MainWindow()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _startWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _startWatchdog.Tick += (_, _) => VerifyPlaybackStarted();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = (MainViewModel)DataContext;
        _viewModel.PlayFileRequested += OnPlayFileRequested;
        _viewModel.StopRequested += OnStopRequested;

        // Start on a random album (auto-plays its first track) as soon as the first frame
        // has rendered. The hidden MediaElement may still drop that first Play() while its
        // pipeline initialises — the start watchdog detects and repairs that, so no blind
        // fixed delay is needed.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => _viewModel?.SelectRandomAlbum()));
    }

    // ----- Playback bridge ---------------------------------------------------

    private void OnPlayFileRequested(string filePath)
    {
        // Defer to let an in-progress album/track selection settle first. Starting the
        // MediaElement synchronously during a selection change leaves it loaded but not
        // actually playing (the reported "must pick another track first" bug). Assigning a
        // new Source implicitly resets playback, so no explicit Stop()/Position is needed.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _hasMedia = true;
            _pendingSource = new Uri(filePath);
            Player.Source = _pendingSource;
            Player.Play();
            SetPlaying(true);
            RestartStartWatchdog();
        }));
    }

    private void OnStopRequested()
    {
        Dispatcher.Invoke(() =>
        {
            _startWatchdog.Stop();
            _pendingSource = null;
            Player.Stop();
            Player.Source = null;
            _hasMedia = false;
            SetPlaying(false);
            ResetTransport();
        });
    }

    private void RestartStartWatchdog()
    {
        _startAttempts = 0;
        _startWatchdog.Stop();
        _startWatchdog.Start();
    }

    /// <summary>
    /// The MediaElement has no "ready" signal, but it is verifiably playing once its
    /// position clock advances. A cold-started (hidden) MediaElement can silently drop
    /// Play() — position then sticks at 0:00. In that case force a full source reload
    /// (what clicking another track effectively did) and try again, bounded.
    /// </summary>
    private void VerifyPlaybackStarted()
    {
        if (!_isPlaying || _pendingSource is null || Player.Position > TimeSpan.Zero)
        {
            _startWatchdog.Stop();
            return;
        }

        if (++_startAttempts >= MaxStartAttempts)
        {
            _startWatchdog.Stop();
            if (_viewModel is not null)
                _viewModel.StatusText = "Wiedergabe konnte nicht gestartet werden.";
            return;
        }

        // Re-assigning the same Uri would be a dependency-property no-op, so clear first.
        Player.Source = null;
        Player.Source = _pendingSource;
        Player.Play();
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (!Player.NaturalDuration.HasTimeSpan)
            return;

        var total = Player.NaturalDuration.TimeSpan;
        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(total.TotalSeconds, 0.1);
        PositionSlider.Value = 0;
        _updatingSlider = false;
        TotalTime.Text = FormatTime(total);
        CurrentTime.Text = "0:00";

        // Ensure playback actually starts once the media is ready (Play() issued before
        // load can otherwise be dropped).
        if (_isPlaying)
            Player.Play();
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e) => _viewModel?.OnTrackEnded();

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // A genuinely unplayable file won't be fixed by the watchdog's reload-retries.
        _startWatchdog.Stop();
        _pendingSource = null;
        if (_viewModel is not null)
            _viewModel.StatusText = $"Wiedergabefehler: {e.ErrorException.Message}";
    }

    // ----- Transport bar -----------------------------------------------------

    private void UpdatePosition()
    {
        if (!_hasMedia || !Player.NaturalDuration.HasTimeSpan)
            return;

        _updatingSlider = true;
        PositionSlider.Value = Player.Position.TotalSeconds;
        _updatingSlider = false;
        CurrentTime.Text = FormatTime(Player.Position);
    }

    private void OnPositionSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || !_hasMedia)
            return;

        // User-initiated seek.
        Player.Position = TimeSpan.FromSeconds(e.NewValue);
        CurrentTime.Text = FormatTime(Player.Position);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (!_hasMedia)
        {
            _viewModel?.PlayCurrentOrFirst();
            return;
        }

        if (_isPlaying)
        {
            Player.Pause();
            SetPlaying(false);
        }
        else
        {
            Player.Play();
            SetPlaying(true);
            RestartStartWatchdog();     // no-ops on first tick if position already advanced
        }
    }

    private bool _isPlaying;

    private void SetPlaying(bool playing)
    {
        _isPlaying = playing;
        PlayPauseButton.Content = playing ? "⏸" : "⏵";
        if (playing)
            _positionTimer.Start();
        else
            _positionTimer.Stop();
    }

    private void ResetTransport()
    {
        _updatingSlider = true;
        PositionSlider.Value = 0;
        _updatingSlider = false;
        CurrentTime.Text = "0:00";
        TotalTime.Text = "0:00";
    }

    private static string FormatTime(TimeSpan t)
        => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ----- Track selection ---------------------------------------------------

    private void OnTrackClicked(object sender, MouseButtonEventArgs e)
    {
        // Single click on a track row plays that track (buttons handle their own clicks).
        if (sender is FrameworkElement { DataContext: TrackViewModel track })
            _viewModel?.PlayTrack(track);
    }

    // ----- Drag & drop ZIP import -------------------------------------------

    private static bool HasZip(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop)
           && ((string[])e.Data.GetData(DataFormats.FileDrop))
               .Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private void OnAlbumListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasZip(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnAlbumListDrop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || !HasZip(e))
            return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        await _viewModel.ImportZipsAsync(files);
    }
}
