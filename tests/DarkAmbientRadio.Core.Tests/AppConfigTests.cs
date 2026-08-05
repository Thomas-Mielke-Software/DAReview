using DarkAmbientRadio.Core.Config;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class AppConfigTests
{
    private static AppConfig WithBase(string cloudBase) => new() { CloudBase = cloudBase };

    [Fact]
    public void Empty_overrides_fall_back_to_the_cloud_base()
    {
        var config = WithBase(@"D:\Nextcloud");

        Assert.Equal(@"D:\Nextcloud\Multimedia\Music\Styles\Dark Ambient", config.ArchiveDir);
        Assert.Equal(@"D:\Nextcloud\Dark Ambient Review", config.ReviewDir);
        Assert.Equal(@"D:\Nextcloud\Dark Ambient 192kbps", config.AirplayDir);
    }

    [Fact]
    public void An_absolute_override_is_taken_as_it_is()
    {
        var config = WithBase(@"D:\Nextcloud");
        config.ArchiveDirOverride = @"E:\Master";

        Assert.Equal(@"E:\Master", config.ArchiveDir);
    }

    // The labels advertise "<Basis>", so a value spelled that way has to work — typing it used to
    // produce a path under the app's working directory and fail every album of the import.
    [Theory]
    [InlineData(@"<Basis>\Dark Ambient")]
    [InlineData(@"<basis>\Dark Ambient")]
    [InlineData(@"  <Basis>/Dark Ambient  ")]
    [InlineData(@"Dark Ambient")]
    public void The_base_placeholder_and_relative_values_resolve_against_the_cloud_base(string value)
    {
        var config = WithBase(@"D:\Nextcloud");
        config.ArchiveDirOverride = value;

        Assert.Equal(@"D:\Nextcloud\Dark Ambient", config.ArchiveDir);
    }

    [Fact]
    public void The_placeholder_on_its_own_is_the_cloud_base()
    {
        var config = WithBase(@"D:\Nextcloud");
        config.ReviewDirOverride = "<Basis>";

        Assert.Equal(@"D:\Nextcloud", Path.TrimEndingDirectorySeparator(config.ReviewDir));
    }
}
