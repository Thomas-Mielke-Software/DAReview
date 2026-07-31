using System.Reflection;

namespace DarkAmbientRadio.Core.Audio;

/// <summary>Resolves external tool executables (ffmpeg, mp3gain).</summary>
public static class ToolLocator
{
    /// <summary>
    /// Resolution order: an explicitly configured path (if it exists) → a bundled copy
    /// under the application's "tools" folder → the bare executable name (relying on PATH).
    /// </summary>
    public static string Resolve(string? configuredPath, string exeName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath!;

        var baseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location
                                            ?? Assembly.GetExecutingAssembly().Location)
                      ?? AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "tools", exeName);
        return File.Exists(bundled) ? bundled : exeName;
    }
}
