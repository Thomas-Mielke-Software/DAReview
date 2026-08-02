namespace DarkAmbientRadio.Core.Files;

/// <summary>Outcome of importing an album folder into the review directory.</summary>
/// <param name="TargetPath">Where the album ended up.</param>
/// <param name="SourceToRemove">
/// Set only when the folder had to be <em>copied</em> (different volume): the caller must ask
/// the user before deleting this leftover source. Null after a plain move.
/// </param>
public readonly record struct FolderImportResult(string TargetPath, string? SourceToRemove)
{
    public bool WasCopied => SourceToRemove is not null;
}

/// <summary>
/// Takes an existing album folder to where the pipeline expects it. Prefers a move (instant,
/// leaves nothing behind); only when source and destination sit on different volumes does it
/// fall back to a recursive copy, and then it reports the source back rather than deleting it
/// itself — removing the user's original is never this class's decision.
/// </summary>
public sealed class FolderImporter
{
    public FolderImportResult Import(
        string sourceFolder,
        string destinationDir,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        sourceFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder));
        destinationDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDir));

        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"Ordner nicht gefunden: {sourceFolder}");

        var name = Path.GetFileName(sourceFolder);
        var target = Path.Combine(destinationDir, name);

        if (IsAtOrUnder(destinationDir, sourceFolder))
            throw new IOException($"»{name}« liegt bereits unterhalb von {destinationDir}.");

        if (Directory.Exists(target))
            throw new IOException($"In {destinationDir} gibt es bereits »{name}«.");

        Directory.CreateDirectory(destinationDir);

        try
        {
            progress?.Report($"Verschiebe {name} …");
            Directory.Move(sourceFolder, target);
            return new FolderImportResult(target, SourceToRemove: null);
        }
        catch (IOException)
        {
            // Most likely a cross-volume move, which Directory.Move refuses outright (it does
            // not touch anything in that case). Copying is the only way across.
        }

        progress?.Report($"Kopiere {name} (anderes Laufwerk) …");
        CopyDirectory(sourceFolder, target, ct);
        return new FolderImportResult(target, SourceToRemove: sourceFolder);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> itself or lives under it.</summary>
    public static bool IsAtOrUnder(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            return false;

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return string.Equals(root, path, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), ct);
        }
    }
}
