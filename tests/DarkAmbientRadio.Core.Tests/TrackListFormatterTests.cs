using DarkAmbientRadio.Core.Naming;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class TrackListFormatterTests
{
    [Theory]
    [InlineData(new[] { 3 }, "und", "3")]
    [InlineData(new[] { 1, 4 }, "und", "1 und 4")]
    [InlineData(new[] { 2, 3, 5 }, "und", "2, 3 und 5")]
    [InlineData(new[] { 1, 4 }, "UND", "1 UND 4")]
    [InlineData(new[] { 5, 3, 2 }, "und", "2, 3 und 5")] // unsorted input is sorted
    public void FormatNumberList_produces_german_reading_style(int[] numbers, string connector, string expected)
        => Assert.Equal(expected, TrackListFormatter.FormatNumberList(numbers, connector));

    [Fact]
    public void BuildSuffix_returns_empty_when_nothing_rejected()
        => Assert.Equal(string.Empty, TrackListFormatter.BuildSuffix(new[] { 1, 2, 3 }, Array.Empty<int>()));

    [Fact]
    public void BuildSuffix_prefers_shorter_OHNE_variant()
    {
        // 10 tracks, 3 rejected -> OHNE is much shorter than listing the other 7.
        var all = Enumerable.Range(1, 10);
        var suffix = TrackListFormatter.BuildSuffix(all, new[] { 2, 3, 5 });
        Assert.Equal(" [OHNE TRACK 2, 3 und 5]", suffix);
    }

    [Fact]
    public void BuildSuffix_prefers_shorter_NUR_variant()
    {
        // 5 tracks, 3 rejected -> keeping {1,4} is shorter than "OHNE 2, 3 und 5".
        var all = Enumerable.Range(1, 5);
        var suffix = TrackListFormatter.BuildSuffix(all, new[] { 2, 3, 5 });
        Assert.Equal(" [NUR TRACK 1 UND 4]", suffix);
    }

    [Fact]
    public void BuildSuffix_falls_back_to_OHNE_when_all_rejected()
    {
        var all = new[] { 1, 2 };
        var suffix = TrackListFormatter.BuildSuffix(all, new[] { 1, 2 });
        Assert.Equal(" [OHNE TRACK 1 und 2]", suffix);
    }
}
