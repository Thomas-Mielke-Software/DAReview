using DarkAmbientRadio.Core.Naming;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

/// <summary>
/// The review list must not depend on a naming convention: an album dropped by hand carries
/// whatever names its source gave it, and used to vanish silently when they did not match the
/// Bandcamp schema.
/// </summary>
public class TrackNumberingTests
{
    private const string Folder = @"C:\Review\Album";

    private static IReadOnlyList<NumberedTrackFile> Assign(params string[] fileNames)
        => TrackNumbering.Assign(fileNames.Select(n => Path.Combine(Folder, n)));

    [Fact]
    public void Bandcamp_schema_names_keep_their_numbers()
    {
        var result = Assign(
            "Ager Sonus - Necropolis - 02 Shards of Umm el-Qaab.mp3",
            "Ager Sonus - Necropolis - 01 Intro.mp3",
            "Ager Sonus - Necropolis - 10 Lost.mp3");

        Assert.Equal(new[] { 1, 2, 10 }, result.Select(r => r.Number).OrderBy(n => n));
        Assert.All(result, r => Assert.Equal(TrackNumberSource.FileName, r.Source));
    }

    [Fact]
    public void A_leading_number_is_enough()
    {
        // Scene-release naming: no " - " anywhere, so the schema parse finds nothing.
        var result = Assign(
            "01_massacre_divino_-_agarez.mp3",
            "02_massacre_divino_-_arnal.mp3");

        Assert.Equal(new[] { 1, 2 }, result.Select(r => r.Number));
        Assert.All(result, r => Assert.Equal(TrackNumberSource.FileName, r.Source));
    }

    [Theory]
    [InlineData("01 Intro.mp3", 1)]
    [InlineData("02.Whispers.mp3", 2)]
    [InlineData("7-Dust.mp3", 7)]
    [InlineData("003_Tomb.mp3", 3)]
    public void Leading_numbers_are_read_through_any_delimiter(string fileName, int expected)
        => Assert.Equal(expected, Assign(fileName).Single().Number);

    [Fact]
    public void A_year_at_the_front_is_not_a_track_number()
    {
        // "200" out of "2001" would be a nonsense number — the file falls through to its position.
        var result = Assign("2001 - A Space Odyssey.mp3").Single();

        Assert.Equal(1, result.Number);
        Assert.Equal(TrackNumberSource.Position, result.Source);
    }

    [Fact]
    public void Nameless_tracks_are_numbered_by_their_place_in_the_folder()
    {
        var result = Assign("Beta.mp3", "Gamma.mp3", "Alpha.mp3");

        Assert.Equal(new[] { "Alpha.mp3", "Beta.mp3", "Gamma.mp3" }, result.Select(r => Path.GetFileName(r.FilePath)));
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(r => r.Number));
        Assert.All(result, r => Assert.Equal(TrackNumberSource.Position, r.Source));
    }

    [Fact]
    public void Known_numbers_survive_a_folder_that_mixes_conventions()
    {
        var result = Assign("01 Intro.mp3", "Bonus Track.mp3", "03 Outro.mp3");

        Assert.Equal(1, Number(result, "01 Intro.mp3"));
        Assert.Equal(3, Number(result, "03 Outro.mp3"));
        // The lowest free number, not "one past the end".
        Assert.Equal(2, Number(result, "Bonus Track.mp3"));
    }

    [Fact]
    public void Duplicate_numbers_do_not_collapse_two_tracks_into_one()
    {
        // Both name themselves track 1; the loser gets the next free number rather than a
        // decision key that is already spoken for.
        var result = Assign("01 First.mp3", "01 Also First.mp3", "02 Second.mp3");

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(r => r.Number).OrderBy(n => n));
        Assert.Equal(1, Number(result, "01 Also First.mp3"));   // first in file-name order
        Assert.Equal(2, Number(result, "02 Second.mp3"));
        Assert.Equal(3, Number(result, "01 First.mp3"));
    }

    [Fact]
    public void An_empty_folder_yields_nothing()
        => Assert.Empty(TrackNumbering.Assign([]));

    private static int Number(IEnumerable<NumberedTrackFile> result, string fileName)
        => result.Single(r => Path.GetFileName(r.FilePath) == fileName).Number;
}
