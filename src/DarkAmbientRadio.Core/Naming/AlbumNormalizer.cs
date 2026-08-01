namespace DarkAmbientRadio.Core.Naming;

/// <summary>What a normalisation run changed.</summary>
/// <param name="FolderPath">The album folder afterwards (may have been renamed).</param>
/// <param name="RenamedFiles">Number of track files whose name changed.</param>
/// <param name="RetaggedFiles">Number of track files whose ID3 tag changed.</param>
/// <param name="FolderRenamed">Whether the album folder itself was renamed.</param>
public readonly record struct NormalizeResult(
    string FolderPath, int RenamedFiles, int RetaggedFiles, bool FolderRenamed)
{
    public bool AnyChange => RenamedFiles > 0 || RetaggedFiles > 0 || FolderRenamed;
}

/// <summary>
/// Fixes the capitalisation of the album title or the artist across an album folder: the
/// folder name, every track filename following the "Artist - Album - NN Title" schema, and
/// the matching ID3 tags. Audio data is never touched — TagLib rewrites only the tag frames.
/// </summary>
public sealed class AlbumNormalizer
{
    /// <summary>Title-cases the album title in the folder name, filenames and ID3 album tag.</summary>
    public NormalizeResult NormalizeAlbumTitle(string folderPath)
        => Normalize(folderPath, NormalizeTarget.Album);

    /// <summary>Title-cases the artist in the folder name, filenames and ID3 artist tags.</summary>
    public NormalizeResult NormalizeArtist(string folderPath)
        => Normalize(folderPath, NormalizeTarget.Artist);

    private enum NormalizeTarget { Album, Artist }

    private static NormalizeResult Normalize(string folderPath, NormalizeTarget target)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Albumordner nicht gefunden: {folderPath}");

        var renamed = 0;
        var retagged = 0;

        foreach (var file in Directory.EnumerateFiles(folderPath, "*.mp3"))
        {
            var currentPath = file;

            var parsed = TrackFileName.TryParse(currentPath);
            if (parsed is not null)
            {
                var fixedName = target == NormalizeTarget.Album
                    ? parsed with { Album = TitleCaseNormalizer.Normalize(parsed.Album) }
                    : parsed with { Artist = TitleCaseNormalizer.Normalize(parsed.Artist) };

                var newFileName = fixedName.ToFileName();
                if (!string.Equals(newFileName, Path.GetFileName(currentPath), StringComparison.Ordinal))
                {
                    var newPath = Path.Combine(folderPath, newFileName);
                    MoveFile(currentPath, newPath);
                    currentPath = newPath;
                    renamed++;
                }
            }

            if (RetagFile(currentPath, target))
                retagged++;
        }

        var (newFolderPath, folderRenamed) = RenameFolder(folderPath, target);
        return new NormalizeResult(newFolderPath, renamed, retagged, folderRenamed);
    }

    private static bool RetagFile(string filePath, NormalizeTarget target)
    {
        // A broken/absent tag must not abort the whole run — the rename already happened.
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;
            var changed = false;

            if (target == NormalizeTarget.Album)
            {
                var normalised = TitleCaseNormalizer.Normalize(tag.Album);
                if (!string.IsNullOrEmpty(tag.Album) && normalised != tag.Album)
                {
                    tag.Album = normalised;
                    changed = true;
                }
            }
            else
            {
                changed |= NormalizeNames(tag.Performers, v => tag.Performers = v);
                changed |= NormalizeNames(tag.AlbumArtists, v => tag.AlbumArtists = v);
            }

            if (changed)
                tagFile.Save();
            return changed;
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            return false;
        }
    }

    private static bool NormalizeNames(string[]? names, Action<string[]> assign)
    {
        if (names is null || names.Length == 0)
            return false;

        var normalised = names.Select(TitleCaseNormalizer.Normalize).ToArray();
        if (normalised.SequenceEqual(names, StringComparer.Ordinal))
            return false;

        assign(normalised);
        return true;
    }

    private static (string Path, bool Renamed) RenameFolder(string folderPath, NormalizeTarget target)
    {
        var parent = Path.GetDirectoryName(folderPath);
        var folderName = Path.GetFileName(folderPath);
        if (parent is null)
            return (folderPath, false);

        var parsed = AlbumFolderName.TryParse(folderName);
        var newName = parsed is null
            // No "Artist - Album" split: only the album button may touch such a folder, and
            // then the whole name is the title.
            ? (target == NormalizeTarget.Album ? TitleCaseNormalizer.Normalize(folderName) : folderName)
            : (target == NormalizeTarget.Album
                ? parsed with { Album = TitleCaseNormalizer.Normalize(parsed.Album) }
                : parsed with { Artist = TitleCaseNormalizer.Normalize(parsed.Artist) }).ToString();

        if (string.Equals(newName, folderName, StringComparison.Ordinal))
            return (folderPath, false);

        var newPath = Path.Combine(parent, newName);
        MoveDirectory(folderPath, newPath);
        return (newPath, true);
    }

    // ----- Case-only renames -------------------------------------------------
    // Windows paths are case-insensitive, so "x.mp3" -> "X.mp3" looks like an existing
    // target to Move() and fails. Detour via a temporary name in that case.

    private static void MoveFile(string source, string target)
        => Move(source, target, (s, t) => File.Move(s, t));

    private static void MoveDirectory(string source, string target)
        => Move(source, target, (s, t) => Directory.Move(s, t));

    private static void Move(string source, string target, Action<string, string> move)
    {
        if (string.Equals(source, target, StringComparison.Ordinal))
            return;

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            var temp = source + ".casefix-" + Guid.NewGuid().ToString("N")[..8];
            move(source, temp);
            move(temp, target);
            return;
        }

        move(source, target);
    }
}
