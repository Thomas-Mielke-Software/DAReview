using Microsoft.VisualBasic.FileIO;

namespace DarkAmbientRadio.Core.Files;

/// <summary>
/// Discards a rejected album by removing its folders. Everything goes to the recycle bin
/// rather than being deleted outright: a review code can only be redeemed once, so a
/// mis-click here would otherwise cost a download for good.
/// </summary>
public sealed class AlbumRemover
{
    /// <summary>
    /// The archive folder holding this album's untouched 320k master, or <c>null</c> when there
    /// is none. Archive and review folder share the name the ZIP had — but the normalisation
    /// buttons rename the review folder only, so after a rename the two no longer match and
    /// nothing is found. Folder imports never produce an archive copy either.
    /// </summary>
    public static string? FindArchiveFolder(string reviewFolderPath, string archiveDir)
    {
        if (string.IsNullOrWhiteSpace(archiveDir))
            return null;

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(reviewFolderPath));
        if (string.IsNullOrEmpty(name))
            return null;

        var candidate = Path.Combine(archiveDir, name);
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>Moves a folder to the recycle bin; a folder that is already gone is not an error.</summary>
    public void Delete(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return;

        FileSystem.DeleteDirectory(
            folderPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }
}
