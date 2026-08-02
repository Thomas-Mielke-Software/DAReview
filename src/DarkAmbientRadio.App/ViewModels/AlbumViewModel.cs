using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DarkAmbientRadio.Core.Models;
using DarkAmbientRadio.Core.Review;

namespace DarkAmbientRadio.App.ViewModels;

public partial class AlbumViewModel : ObservableObject
{
    private readonly ReviewStore _store;

    public AlbumViewModel(Album album, ReviewStore store)
    {
        Album = album;
        _store = store;

        Tracks = new ObservableCollection<TrackViewModel>(
            album.Tracks.Select(t =>
            {
                var vm = new TrackViewModel(album, t, store, RefreshDecisionState);
                vm.InitializeDecision();
                return vm;
            }));

        RefreshListenPercent();
        RefreshDecisionState();
    }

    public Album Album { get; }
    public string Name => Album.Name;
    public ObservableCollection<TrackViewModel> Tracks { get; }

    [ObservableProperty]
    private int _listenPercent;

    [ObservableProperty]
    private bool _canPublish;

    public string ListenPercentText => $"{ListenPercent} %";

    partial void OnListenPercentChanged(int value) => OnPropertyChanged(nameof(ListenPercentText));

    /// <summary>Records that one more track finished playing and updates the listen counter.</summary>
    public void RecordTrackPlayed()
    {
        _store.RecordTrackPlayed(Album);
        RefreshListenPercent();
    }

    /// <summary>
    /// Approves everything still undecided in one go; already rejected tracks keep their
    /// decision (a stray click here must not silently undo a reject).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApproveAll))]
    private void ApproveAll()
    {
        if (_store.ApproveUndecided(Album) == 0)
            return;

        foreach (var track in Tracks)
            track.InitializeDecision();

        RefreshDecisionState();
    }

    private bool CanApproveAll() => Tracks.Any(t => t.Decision == TrackDecision.Undecided);

    private void RefreshListenPercent() => ListenPercent = Album.ListenPercent;

    private void RefreshDecisionState()
    {
        CanPublish = Album.AllTracksDecided && !Album.State.Published;
        ApproveAllCommand.NotifyCanExecuteChanged();
    }
}
