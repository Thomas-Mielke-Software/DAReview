using DarkAmbientRadio.Core.Files;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class AlbumRemoverTests : IDisposable
{
    private readonly string _root;
    private readonly string _reviewDir;
    private readonly string _archiveDir;

    public AlbumRemoverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dar_rm_" + Guid.NewGuid().ToString("N"));
        _reviewDir = Path.Combine(_root, "Review");
        _archiveDir = Path.Combine(_root, "Archive");
        Directory.CreateDirectory(_reviewDir);
        Directory.CreateDirectory(_archiveDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Archive_folder_is_found_by_the_shared_album_name()
    {
        var review = Directory.CreateDirectory(Path.Combine(_reviewDir, "Artist - Album")).FullName;
        var archive = Directory.CreateDirectory(Path.Combine(_archiveDir, "Artist - Album")).FullName;

        Assert.Equal(archive, AlbumRemover.FindArchiveFolder(review, _archiveDir));
    }

    [Fact]
    public void No_archive_folder_is_reported_when_the_names_diverge()
    {
        // What a normalisation run leaves behind: the review folder was renamed, the archive
        // folder keeps the spelling the ZIP had. Nothing must be deleted on a guess.
        var review = Directory.CreateDirectory(Path.Combine(_reviewDir, "Artist - Album")).FullName;
        Directory.CreateDirectory(Path.Combine(_archiveDir, "ARTIST - album (pre-order)"));

        Assert.Null(AlbumRemover.FindArchiveFolder(review, _archiveDir));
    }

    [Fact]
    public void No_archive_folder_is_reported_when_the_archive_is_empty_or_unset()
    {
        var review = Directory.CreateDirectory(Path.Combine(_reviewDir, "Artist - Album")).FullName;

        Assert.Null(AlbumRemover.FindArchiveFolder(review, _archiveDir));
        Assert.Null(AlbumRemover.FindArchiveFolder(review, ""));
    }

    [Fact]
    public void Deleting_a_folder_that_is_already_gone_is_not_an_error()
    {
        new AlbumRemover().Delete(Path.Combine(_reviewDir, "never existed"));
    }
}
