namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// Derives the album folder name from a downloaded ZIP file name by dropping the
/// ".zip" extension and any trailing " (pre-order)" marker Bandcamp appends.
/// </summary>
public static class ArchiveNaming
{
    private const string PreOrderSuffix = " (pre-order)";

    public static string DeriveFolderName(string zipFileName)
    {
        var name = Path.GetFileName(zipFileName);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        if (name.EndsWith(PreOrderSuffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^PreOrderSuffix.Length];

        return name.TrimEnd();
    }
}
