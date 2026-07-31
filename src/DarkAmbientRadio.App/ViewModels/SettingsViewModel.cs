using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DarkAmbientRadio.Core.Config;

namespace DarkAmbientRadio.App.ViewModels;

/// <summary>Editable copy of <see cref="AppConfig"/> for the settings dialog.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;

    public SettingsViewModel(AppConfig config)
    {
        _config = config;
        CloudBase = config.CloudBase;
        ArchiveDirOverride = config.ArchiveDirOverride ?? string.Empty;
        ReviewDirOverride = config.ReviewDirOverride ?? string.Empty;
        AirplayDirOverride = config.AirplayDirOverride ?? string.Empty;
        DownloadDirOverride = config.DownloadDirOverride ?? string.Empty;
        FfmpegPath = config.FfmpegPath ?? string.Empty;
        Mp3gainPath = config.Mp3gainPath ?? string.Empty;
        Bitrate = config.Bitrate;
        NormalizationDb = config.NormalizationDb.ToString(CultureInfo.CurrentCulture);
        CodeRegex = config.CodeRegex;
        ConnectorOhne = config.ConnectorOhne;
        ConnectorNur = config.ConnectorNur;
    }

    [ObservableProperty] private string _cloudBase = string.Empty;
    [ObservableProperty] private string _archiveDirOverride = string.Empty;
    [ObservableProperty] private string _reviewDirOverride = string.Empty;
    [ObservableProperty] private string _airplayDirOverride = string.Empty;
    [ObservableProperty] private string _downloadDirOverride = string.Empty;
    [ObservableProperty] private string _ffmpegPath = string.Empty;
    [ObservableProperty] private string _mp3gainPath = string.Empty;
    [ObservableProperty] private string _bitrate = "192k";
    [ObservableProperty] private string _normalizationDb = "86";
    [ObservableProperty] private string _codeRegex = string.Empty;
    [ObservableProperty] private string _connectorOhne = "und";
    [ObservableProperty] private string _connectorNur = "UND";

    /// <summary>Writes the edited values back into the underlying config.</summary>
    public void ApplyTo()
    {
        _config.CloudBase = CloudBase.Trim();
        _config.ArchiveDirOverride = NullIfBlank(ArchiveDirOverride);
        _config.ReviewDirOverride = NullIfBlank(ReviewDirOverride);
        _config.AirplayDirOverride = NullIfBlank(AirplayDirOverride);
        _config.DownloadDirOverride = NullIfBlank(DownloadDirOverride);
        _config.FfmpegPath = NullIfBlank(FfmpegPath);
        _config.Mp3gainPath = NullIfBlank(Mp3gainPath);
        _config.Bitrate = Bitrate.Trim();
        if (double.TryParse(NormalizationDb, NumberStyles.Float, CultureInfo.CurrentCulture, out var db))
            _config.NormalizationDb = db;
        _config.CodeRegex = CodeRegex.Trim();
        _config.ConnectorOhne = ConnectorOhne.Trim();
        _config.ConnectorNur = ConnectorNur.Trim();
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
