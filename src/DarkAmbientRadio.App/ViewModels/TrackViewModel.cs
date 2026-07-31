using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Review;

namespace DarkAmbientRadio.App.ViewModels;

public partial class TrackViewModel : ObservableObject
{
    private readonly Album _album;
    private readonly TrackItem _track;
    private readonly ReviewStore _store;
    private readonly Action _onDecisionChanged;

    public TrackViewModel(Album album, TrackItem track, ReviewStore store, Action onDecisionChanged)
    {
        _album = album;
        _track = track;
        _store = store;
        _onDecisionChanged = onDecisionChanged;
    }

    public TrackItem Track => _track;
    public int Number => _track.TrackNumber;
    public string FileName => _track.FileName;
    public string FilePath => _track.FilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApproved))]
    [NotifyPropertyChangedFor(nameof(IsRejected))]
    private TrackDecision _decision = TrackDecision.Undecided;

    public bool IsApproved => Decision == TrackDecision.Approved;
    public bool IsRejected => Decision == TrackDecision.Rejected;

    /// <summary>Highlights the track that is currently playing.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    public void InitializeDecision() => Decision = _track.Decision;

    [RelayCommand]
    private void Approve() => SetDecision(TrackDecision.Approved);

    [RelayCommand]
    private void Reject() => SetDecision(TrackDecision.Rejected);

    private void SetDecision(TrackDecision decision)
    {
        // Toggle back to undecided when the same button is pressed again.
        var target = Decision == decision ? TrackDecision.Undecided : decision;
        _store.SetDecision(_album, _track, target);
        Decision = target;
        _onDecisionChanged();
    }
}
