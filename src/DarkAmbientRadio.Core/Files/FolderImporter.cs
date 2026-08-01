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
/// Takes an existing album folder into the review queue. Prefers a move (instant, leaves
/// nothing behind); only when source and destination sit on different volumes does it fall
/// back to a recursive copy, and then it reports the source back rather than deleting it
/// itself — removing the user's original is never this class's decision.
/// </summary>
public sealed class FolderImporter
{
    public FolderImportResult Import(
        string sourceFolder,
        string reviewDir,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        sourceFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder));
        reviewDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reviewDir));

        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"Ordner nicht gefunden: {sourceFolder}");

        var name = Path.GetFileName(sourceFolder);
        var target = Path.Combine(reviewDir, name);

        if (string.Equals(sourceFolder, target, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"»{name}« liegt bereits im Review-Ordner.");

        if (IsInside(reviewDir, sourceFolder))
            throw new IOException($"»{name}« liegt bereits unterhalb des Review-Ordners.");

        if (Directory.Exists(target))
            throw new IOException($"Im Review-Ordner gibt es bereits »{name}«.");

        Directory.CreateDirectory(reviewDir);

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

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or lives under it.</summary>
    private static bool IsInside(string root, string path)
        => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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
