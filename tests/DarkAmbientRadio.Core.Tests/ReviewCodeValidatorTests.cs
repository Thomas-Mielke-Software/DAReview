using DarkAmbientRadio.Core.Sources;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class ReviewCodeValidatorTests
{
    private readonly ReviewCodeValidator _validator = new();

    [Theory]
    [InlineData("12ab-3cd4")]
    [InlineData("0000-0000")]
    [InlineData("abcd-efgh")]
    public void Accepts_wellformed_codes(string code)
        => Assert.True(_validator.IsValid(code));

    [Fact]
    public void Normalizes_case_and_whitespace()
    {
        Assert.True(_validator.TryNormalize("  12AB-3CD4 ", out var normalized));
        Assert.Equal("12ab-3cd4", normalized);
    }

    [Theory]
    [InlineData("12ab3cd4")]      // missing dash
    [InlineData("12ab-3cd")]      // too short
    [InlineData("12ab-3cd45")]    // too long
    [InlineData("12ab_3cd4")]     // wrong separator
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_malformed_codes(string? code)
        => Assert.False(_validator.IsValid(code));
}
