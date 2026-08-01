using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DarkAmbientRadio.App.ViewModels;
using DarkAmbientRadio.Core.Config;
using Microsoft.Win32;

namespace DarkAmbientRadio.App;

/// <summary>
/// Interaction logic for MainWindow.xaml. Bridges the MVVM view model to the
/// code-controlled <c>MediaElement</c> and drives the transport bar (play/pause,
/// seekable position slider).
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxStartAttempts = 10;

    /// <summary>
    /// Grace period after waking from standby before touching the MediaElement — the audio
    /// device is not necessarily back yet at the moment the Resume event fires.
    /// </summary>
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(2);

    private MainViewModel? _viewModel;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _startWatchdog;
    private readonly DispatcherTimer _resumeTimer;
    private Uri? _currentSource;    // what is loaded; the watchdog and resume both re-load it
    private TimeSpan? _resumePosition;  // seek target applied once the reloaded media opens
    private TimeSpan _watchdogBaseline; // position when Play() was issued
    private int _startAttempts;
    private bool _updatingSlider;   // guards programmatic slider updates from re-seeking
    private bool _hasMedia;
    private bool _mediaFailed;

    public MainWindow()
    {
        InitializeComponent();
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _startWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _startWatchdog.Tick += (_, _) => VerifyPlaybackStarted();
        _resumeTimer = new DispatcherTimer { Interval = ResumeDelay };
        _resumeTimer.Tick += (_, _) => RecoverAfterResume();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = (MainViewModel)DataContext;
        _viewModel.PlayFileRequested += OnPlayFileRequested;
        _viewModel.StopRequested += OnStopRequested;

        RestoreWindowPlacement();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Start on a random album (auto-plays its first track) as soon as the first frame
        // has rendered. The hidden MediaElement may still drop that first Play() while its
        // pipeline initialises — the start watchdog detects and repairs that, so no blind
        // fixed delay is needed.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => _viewModel?.SelectRandomAlbum()));
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;   // static event: would leak the window
        SaveWindowPlacement();
    }

    // ----- Standby / resume --------------------------------------------------

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        // Raised on a system thread, and too early to act on — defer to the UI thread and
        // give the audio stack a moment to come back.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _resumeTimer.Stop();
            _resumeTimer.Start();
        }));
    }

    /// <summary>
    /// Standby tears the audio session down under the MediaElement, which then either sits
    /// dead or has already raised MediaFailed. Reload the same file, seek back to where we
    /// were and carry on.
    /// </summary>
    private void RecoverAfterResume()
    {
        _resumeTimer.Stop();

        if (!_hasMedia || _currentSource is null)
            return;

        var wasPlaying = _isPlaying;
        _resumePosition = Player.Position;

        Player.Stop();
        Player.Source = null;
        _mediaFailed = false;
        Player.Source = _currentSource;

        if (!wasPlaying)
            return;

        Player.Play();
        SetPlaying(true);
        RestartStartWatchdog();
        if (_viewModel is not null)
            _viewModel.StatusText = "Wiedergabe nach Standby fortgesetzt.";
    }

    // ----- Window placement --------------------------------------------------

    private void RestoreWindowPlacement()
    {
        var placement = _viewModel?.Config.Window;
        if (placement is null)
            return;

        if (!placement.IsOnScreen(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
            return;   // saved on a monitor that is no longer there — keep the default position

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        if (_viewModel is null)
            return;

        // RestoreBounds carries the un-maximised geometry; Left/Top/Width/Height would report
        // the maximised frame and the window could never be un-maximised on the next start.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _viewModel.Config.Window = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };

        try
        {
            _viewModel.Config.Save();
        }
        catch (IOException)
        {
            // Losing the window position is not worth blocking shutdown over.
        }
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
            _mediaFailed = false;
            _resumePosition = null;
            _currentSource = new Uri(filePath);
            Player.Source = _currentSource;
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
            _resumeTimer.Stop();
            _currentSource = null;
            _resumePosition = null;
            _mediaFailed = false;
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
        _watchdogBaseline = Player.Position;
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
        // Compare against the position at Play() time, not against zero: after a resume we
        // restart mid-track, where "position > 0" would look like success straight away.
        if (!_isPlaying || _mediaFailed || _currentSource is null || Player.Position > _watchdogBaseline)
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
        Player.Source = _currentSource;
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

        // Restore where standby interrupted us. Seeking is only reliable once the media is
        // actually open, hence here rather than right after re-assigning Source.
        if (_resumePosition is { } resume)
        {
            _resumePosition = null;
            if (resume < total)
            {
                Player.Position = resume;
                _watchdogBaseline = resume;
                UpdatePosition();
            }
        }

        // Ensure playback actually starts once the media is ready (Play() issued before
        // load can otherwise be dropped).
        if (_isPlaying)
            Player.Play();
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e) => _viewModel?.OnTrackEnded();

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // A genuinely unplayable file won't be fixed by the watchdog's reload-retries. Keep
        // _currentSource though — a standby teardown also lands here, and the resume handler
        // needs to know what to reload.
        _startWatchdog.Stop();
        _mediaFailed = true;
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

    // ----- Drag & drop import (ZIPs and album folders) ----------------------

    private static string[] DroppedPaths(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (string[])e.Data.GetData(DataFormats.FileDrop)
            : Array.Empty<string>();

    private static bool HasImportable(DragEventArgs e)
        => DroppedPaths(e).Any(p => Directory.Exists(p)
                                    || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private void OnAlbumListDragOver(object sender, DragEventArgs e)
    {
        // Folders are moved, not copied — but DragDropEffects.Move would make Explorer itself
        // remove the source, which is the importer's job (and needs the user's consent).
        e.Effects = HasImportable(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnAlbumListDrop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || !HasImportable(e))
            return;
        await _viewModel.ImportDroppedAsync(DroppedPaths(e));
    }
}
