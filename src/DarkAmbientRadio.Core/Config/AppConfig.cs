using System.Text.Json;
using System.Text.Json.Serialization;
using DarkAmbientRadio.Core.Sources;

namespace DarkAmbientRadio.Core.Config;

/// <summary>
/// User-editable configuration. All working directories derive from <see cref="CloudBase"/>
/// unless individually overridden. Persisted to %APPDATA%\DarkAmbientRadio\config.json.
/// </summary>
public sealed class AppConfig
{
    // ----- Directories -------------------------------------------------------

    /// <summary>Cloud base directory; every other directory derives from it by default.</summary>
    public string CloudBase { get; set; } = @"D:\Nextcloud";

    /// <summary>Untouched MP3 320 master archive. Default: &lt;CloudBase&gt;\Multimedia\Music\Styles\Dark Ambient.</summary>
    public string? ArchiveDirOverride { get; set; }

    /// <summary>192k normalised review queue. Default: &lt;CloudBase&gt;\Dark Ambient Review.</summary>
    public string? ReviewDirOverride { get; set; }

    /// <summary>Approved, renamed broadcast folder. Default: &lt;CloudBase&gt;\Dark Ambient 192kbps.</summary>
    public string? AirplayDirOverride { get; set; }

    /// <summary>Where downloaded ZIPs land. Default: %USERPROFILE%\Downloads.</summary>
    public string? DownloadDirOverride { get; set; }

    // ----- External tools ----------------------------------------------------

    /// <summary>Path to ffmpeg. Default: bundled tools\ffmpeg.exe, then PATH.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>Path to mp3gain. Default: bundled tools\mp3gain.exe, then PATH.</summary>
    public string? Mp3gainPath { get; set; }

    // ----- Audio & validation ------------------------------------------------

    public string Bitrate { get; set; } = "192k";

    /// <summary>mp3gain target loudness in dB (ReplayGain reference is 89 dB).</summary>
    public double NormalizationDb { get; set; } = 86.0;

    public string CodeRegex { get; set; } = ReviewCodeValidator.DefaultPattern;

    // ----- Folder naming -----------------------------------------------------

    public string ConnectorOhne { get; set; } = "und";
    public string ConnectorNur { get; set; } = "UND";

    // ----- Window -------------------------------------------------------------

    /// <summary>Last main-window placement; null until the window has been closed once.</summary>
    public WindowPlacement? Window { get; set; }

    // ----- Resolved (non-serialised) accessors -------------------------------

    [JsonIgnore]
    public string ArchiveDir => Resolve(ArchiveDirOverride, Path.Combine("Multimedia", "Music", "Styles", "Dark Ambient"));

    [JsonIgnore]
    public string ReviewDir => Resolve(ReviewDirOverride, "Dark Ambient Review");

    [JsonIgnore]
    public string AirplayDir => Resolve(AirplayDirOverride, "Dark Ambient 192kbps");

    [JsonIgnore]
    public string DownloadDir => string.IsNullOrWhiteSpace(DownloadDirOverride)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        : DownloadDirOverride!;

    /// <summary>Delta passed to mp3gain's -d flag (target minus the 89 dB reference).</summary>
    [JsonIgnore]
    public double Mp3GainDelta => NormalizationDb - 89.0;

    private string Resolve(string? overrideValue, string defaultSubfolder)
        => string.IsNullOrWhiteSpace(overrideValue)
            ? Path.Combine(CloudBase, defaultSubfolder)
            : overrideValue!;

    // ----- Persistence -------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DarkAmbientRadio", "config.json");

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultConfigPath;
        if (!File.Exists(path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
