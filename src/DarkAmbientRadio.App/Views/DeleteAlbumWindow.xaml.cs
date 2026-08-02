using System.Windows;

namespace DarkAmbientRadio.App.Views;

/// <summary>
/// Confirmation for discarding an album, with the optional "delete the archived master too"
/// switch. Cancel holds the focus, so a reflex Enter/Space aborts instead of deleting.
/// </summary>
public partial class DeleteAlbumWindow : Window
{
    public DeleteAlbumWindow(string albumName, string reviewFolder, string? archiveFolder)
    {
        InitializeComponent();

        AlbumNameText.Text = albumName;
        ReviewPathText.Text = reviewFolder;

        if (archiveFolder is not null)
        {
            ArchiveCheckBox.Visibility = Visibility.Visible;
            ArchivePathText.Visibility = Visibility.Visible;
            ArchivePathText.Text = archiveFolder;
        }
        else
        {
            NoArchiveText.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>True when the archived 320k master should go as well.</summary>
    public bool AlsoDeleteArchive => ArchiveCheckBox.IsChecked == true;

    private void OnDelete(object sender, RoutedEventArgs e) => DialogResult = true;
}
