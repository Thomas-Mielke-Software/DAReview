using System.IO.Compression;
using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Files;

/// <summary>Unpacks a downloaded Bandcamp ZIP into the archive directory.</summary>
public sealed class ArchiveExtractor
{
    /// <summary>
    /// Extracts <paramref name="zipPath"/> into &lt;archiveDir&gt;\&lt;derived name&gt; and
    /// returns the created album folder path. The folder name drops ".zip" and any
    /// trailing " (pre-order)" marker.
    /// </summary>
    public string Extract(string zipPath, string archiveDir)
    {
        var folderName = ArchiveNaming.DeriveFolderName(zipPath);
        var destination = Path.Combine(archiveDir, folderName);
        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(zipPath, destination, overwriteFiles: true);
        return destination;
    }
}
