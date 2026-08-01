using DarkAmbientRadio.Core.Files;

namespace DarkAmbientRadio.Core.Tests;

public class FolderImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dar-import-" + Guid.NewGuid().ToString("N")[..8]);

    private string NewFolder(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(dir, file), "x");
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Import_MovesTheFolderAndLeavesNothingBehind()
    {
        var source = NewFolder("src/Artist - Album", "01 One.mp3", "cover.jpg");
        var reviewDir = Path.Combine(_root, "review");

        var result = new FolderImporter().Import(source, reviewDir);

        Assert.False(result.WasCopied);
        Assert.Null(result.SourceToRemove);
        Assert.False(Directory.Exists(source));
        Assert.Equal(Path.Combine(reviewDir, "Artist - Album"), result.TargetPath);
        Assert.True(File.Exists(Path.Combine(result.TargetPath, "01 One.mp3")));
        Assert.True(File.Exists(Path.Combine(result.TargetPath, "cover.jpg")));
    }

    [Fact]
    public void Import_CreatesTheReviewDirWhenMissing()
    {
        var source = NewFolder("src/Artist - Album", "01 One.mp3");
        var reviewDir = Path.Combine(_root, "does", "not", "exist");

        var result = new FolderImporter().Import(source, reviewDir);

        Assert.True(Directory.Exists(result.TargetPath));
    }

    [Fact]
    public void Import_RefusesWhenTheAlbumIsAlreadyInReview()
    {
        var source = NewFolder("src/Artist - Album", "01 One.mp3");
        var reviewDir = Path.Combine(_root, "review");
        NewFolder("review/Artist - Album", "01 One.mp3");

        var ex = Assert.Throws<IOException>(() => new FolderImporter().Import(source, reviewDir));

        Assert.Contains("bereits", ex.Message);
        Assert.True(Directory.Exists(source));   // nothing was touched
    }

    [Fact]
    public void Import_RefusesAFolderThatAlreadyLivesUnderTheReviewDir()
    {
        var reviewDir = Path.Combine(_root, "review");
        var source = NewFolder("review/Artist - Album", "01 One.mp3");

        Assert.Throws<IOException>(() => new FolderImporter().Import(source, reviewDir));
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public void Import_ThrowsForAMissingSource()
        => Assert.Throws<DirectoryNotFoundException>(
            () => new FolderImporter().Import(Path.Combine(_root, "ghost"), Path.Combine(_root, "review")));

    [Fact]
    public void Import_KeepsNestedSubfolders()
    {
        // NB: this takes the move path — the copy fallback needs two volumes and is not
        // covered here.
        var source = NewFolder("src/Artist - Album", "01 One.mp3");
        Directory.CreateDirectory(Path.Combine(source, "scans"));
        File.WriteAllText(Path.Combine(source, "scans", "back.jpg"), "x");

        var result = new FolderImporter().Import(source, Path.Combine(_root, "review"));

        Assert.True(File.Exists(Path.Combine(result.TargetPath, "scans", "back.jpg")));
    }
}
