using DarkAmbientRadio.Core.Naming;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class TrackNumberParserTests
{
    [Theory]
    [InlineData("Ager Sonus - Necropolis - 02 Shards of Umm el-Qaab.mp3", 2)]
    [InlineData("Ager Sonus - Necropolis - 10 Lost.mp3", 10)]
    [InlineData("Artist - Album - 01 Intro.mp3", 1)]
    public void TryParse_reads_number_after_last_separator(string fileName, int expected)
        => Assert.Equal(expected, TrackNumberParser.TryParse(fileName));

    [Theory]
    [InlineData("cover.jpg")]
    [InlineData("Ager Sonus - Necropolis - Thank you.jpg")]
    [InlineData("Artist - Album - Wallpaper.jpg")]
    public void TryParse_returns_null_for_non_numbered_files(string fileName)
        => Assert.Null(TrackNumberParser.TryParse(fileName));
}
