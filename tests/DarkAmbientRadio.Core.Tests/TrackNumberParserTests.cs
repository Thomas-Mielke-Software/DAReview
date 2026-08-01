using DarkAmbientRadio.Core.Naming;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class TrackNumberParserTests
{
    [Theory]
    [InlineData("Ager Sonus - Necropolis - 02 Shards of Umm el-Qaab.mp3", 2)]
    [InlineData("Ager Sonus - Necropolis - 10 Lost.mp3", 10)]
    [InlineData("Artist - Album - 01 Intro.mp3", 1)]
    public void TryParse_reads_the_number_opening_the_title(string fileName, int expected)
        => Assert.Equal(expected, TrackNumberParser.TryParse(fileName));

    [Theory]
    // Compilation: the title itself contains " - ", so the number is NOT after the last one.
    [InlineData("Cryo Chamber - Tomb of Primordials - 01 Dahlia's Tear - Crystal Scars Beneath a Bleak Sky.mp3", 1)]
    [InlineData("Cryo Chamber - Tomb of Primordials - 03 Svartsinn & Letum - One By One I Broke their Wings.mp3", 3)]
    [InlineData("Cryo Chamber - Tomb of Primordials - 04 New Risen Throne & Mortiis - Chants for Isimud.mp3", 4)]
    public void TryParse_handles_titles_containing_the_separator(string fileName, int expected)
        => Assert.Equal(expected, TrackNumberParser.TryParse(fileName));

    [Theory]
    [InlineData("cover.jpg")]
    [InlineData("Ager Sonus - Necropolis - Thank you.jpg")]
    [InlineData("Artist - Album - Wallpaper.jpg")]
    public void TryParse_returns_null_for_non_numbered_files(string fileName)
        => Assert.Null(TrackNumberParser.TryParse(fileName));
}
