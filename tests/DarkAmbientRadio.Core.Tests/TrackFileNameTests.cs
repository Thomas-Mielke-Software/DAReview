using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Tests;

public class TrackFileNameTests
{
    [Fact]
    public void TryParse_SplitsTheStandardSchema()
    {
        var parsed = TrackFileName.TryParse(@"C:\x\Kolldskygge - Eternal Void - 03 Descent.mp3");

        Assert.NotNull(parsed);
        Assert.Equal("Kolldskygge", parsed!.Artist);
        Assert.Equal("Eternal Void", parsed.Album);
        Assert.Equal("03 Descent", parsed.NumberedTitle);
        Assert.Equal(".mp3", parsed.Extension);
    }

    [Fact]
    public void TryParse_KeepsSeparatorsInsideTheAlbumTitle()
    {
        // Everything before the numbered segment (minus the artist) is the album.
        var parsed = TrackFileName.TryParse("Artist - Album - Part Two - 01 Intro.mp3");

        Assert.NotNull(parsed);
        Assert.Equal("Artist", parsed!.Artist);
        Assert.Equal("Album - Part Two", parsed.Album);
        Assert.Equal("01 Intro", parsed.NumberedTitle);
    }

    [Fact]
    public void TryParse_KeepsSeparatorsInsideTheTrackTitle()
    {
        // Real compilation file: "Label - Album - NN TrackArtist - Title".
        var parsed = TrackFileName.TryParse(
            "Cryo Chamber - Tomb of Primordials - 01 Dahlia's Tear - Crystal Scars Beneath a Bleak Sky.mp3");

        Assert.NotNull(parsed);
        Assert.Equal("Cryo Chamber", parsed!.Artist);
        Assert.Equal("Tomb of Primordials", parsed.Album);
        Assert.Equal("01 Dahlia's Tear - Crystal Scars Beneath a Bleak Sky", parsed.NumberedTitle);
        Assert.Equal(1, parsed.TrackNumber);
    }

    [Fact]
    public void TryParse_ReturnsNullForUnnumberedExtras()
        => Assert.Null(TrackFileName.TryParse("Cryo Chamber - Tomb of Primordials - Thank you.jpg"));

    [Fact]
    public void TryParse_YieldsEmptyAlbumWhenOnlyOneSeparator()
    {
        var parsed = TrackFileName.TryParse("Artist - 01 Intro.mp3");

        Assert.NotNull(parsed);
        Assert.Equal("Artist", parsed!.Artist);
        Assert.Equal(string.Empty, parsed.Album);
        Assert.Equal("01 Intro", parsed.NumberedTitle);
    }

    [Fact]
    public void TryParse_ReturnsNullWithoutSeparator()
        => Assert.Null(TrackFileName.TryParse("cover.mp3"));

    [Theory]
    [InlineData("Artist - Album - 01 Intro.mp3")]
    [InlineData("Artist - Album - Part Two - 01 Intro.mp3")]
    [InlineData("Artist - 01 Intro.mp3")]
    [InlineData("Cryo Chamber - Tomb of Primordials - 01 Dahlia's Tear - Crystal Scars.mp3")]
    public void ToFileName_RoundTripsParse(string fileName)
        => Assert.Equal(fileName, TrackFileName.TryParse(fileName)!.ToFileName());

    [Fact]
    public void ToFileName_ReflectsANormalisedSegment()
    {
        var parsed = TrackFileName.TryParse("ARTIST - ETERNAL VOID - 03 Descent.mp3")!;
        var fixedName = parsed with { Album = TitleCaseNormalizer.Normalize(parsed.Album) };

        Assert.Equal("ARTIST - Eternal Void - 03 Descent.mp3", fixedName.ToFileName());
    }

    [Fact]
    public void AlbumFolderName_SplitsOnTheFirstSeparator()
    {
        var parsed = AlbumFolderName.TryParse("Artist - Album - Part Two");

        Assert.NotNull(parsed);
        Assert.Equal("Artist", parsed!.Artist);
        Assert.Equal("Album - Part Two", parsed.Album);
        Assert.Equal("Artist - Album - Part Two", parsed.ToString());
    }

    [Fact]
    public void AlbumFolderName_ReturnsNullWithoutSeparator()
        => Assert.Null(AlbumFolderName.TryParse("Just An Album"));
}
