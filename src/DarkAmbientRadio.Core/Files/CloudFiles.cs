namespace DarkAmbientRadio.Core.Files;

/// <summary>
/// Deals with Nextcloud's virtual files. A synced folder may hold files that exist only as
/// placeholders — cover art especially, since nothing ever opens it — and their content has to
/// be fetched before ffmpeg or <see cref="File.Copy"/> can read them.
/// <para>
/// The fetch is requested by <em>pinning</em> the file (what Explorer's "Immer auf diesem Gerät
/// behalten" does) and then waiting for the placeholder flags to clear. Simply reading the file
/// would work too, but that is an app-triggered on-demand download, and Windows blocks an app
/// that issues too many of those — see Einstellungen → Datenschutz und Sicherheit → Automatische
/// Dateidownloads. Retrying a failed read is the fastest way into that block, so this class
/// waits instead of hammering.
/// </para>
/// </summary>
public static class CloudFiles
{
    // Cloud provider attributes that .NET does not name.
    private const FileAttributes Pinned = (FileAttributes)0x00080000;
    private const FileAttributes Unpinned = (FileAttributes)0x00100000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>True when the file is a cloud placeholder whose content is not on disk yet.</summary>
    public static bool IsPlaceholder(string filePath)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            return (attributes & (FileAttributes.Offline | RecallOnDataAccess)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes sure the file's content is on disk, doing nothing for ordinary local files.
    /// </summary>
    /// <exception cref="IOException">
    /// The provider did not deliver within <paramref name="timeout"/>. The message names the two
    /// things that actually cause this, because neither is guessable from a Win32 error.
    /// </exception>
    public static void Hydrate(string filePath, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!IsPlaceholder(filePath))
            return;

        var original = File.GetAttributes(filePath);
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        try
        {
            TrySetAttributes(filePath, (original & ~Unpinned) | Pinned);

            while (IsPlaceholder(filePath))
            {
                ct.ThrowIfCancellationRequested();

                if (DateTime.UtcNow >= deadline)
                    throw new IOException(
                        $"»{Path.GetFileName(filePath)}« ließ sich nicht aus der Cloud laden. "
                        + "Entweder hat Nextcloud den Ordner noch nicht fertig synchronisiert, "
                        + "oder Windows blockiert automatische Dateidownloads für diese App "
                        + "(Einstellungen → Datenschutz und Sicherheit → Automatische Dateidownloads).");

                Thread.Sleep(PollInterval);
            }
        }
        finally
        {
            // Leave the user's storage policy as we found it; the content stays on disk either
            // way until the sync client decides to free up space again.
            if ((original & Pinned) == 0)
                TrySetAttributes(filePath, File.GetAttributes(filePath) & ~Pinned);
        }
    }

    /// <summary>
    /// Materialises every placeholder directly inside <paramref name="folder"/>. Worth doing in
    /// one pass <em>before</em> moving a folder around inside the sync root: once moved, the
    /// provider has to replay the move on the server before it can serve the content at all.
    /// </summary>
    public static void HydrateFolder(
        string folder,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsPlaceholder(file))
                continue;

            progress?.Report($"Lade {Path.GetFileName(file)} aus der Cloud …");
            Hydrate(file, ct: ct);
        }
    }

    /// <summary>Copies a file, materialising a placeholder source first.</summary>
    public static void Copy(string source, string target, CancellationToken ct = default)
    {
        Hydrate(source, ct: ct);
        File.Copy(source, target, overwrite: true);
    }

    private static void TrySetAttributes(string filePath, FileAttributes attributes)
    {
        try
        {
            File.SetAttributes(filePath, attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not every provider accepts a pin request; the wait below still gets its chance.
        }
    }
}
