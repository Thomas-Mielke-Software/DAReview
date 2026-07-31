using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    private void RefreshListenPercent() => ListenPercent = Album.ListenPercent;

    private void RefreshDecisionState() => CanPublish = Album.AllTracksDecided && !Album.State.Published;
}
