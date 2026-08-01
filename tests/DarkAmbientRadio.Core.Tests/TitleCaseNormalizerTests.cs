using DarkAmbientRadio.Core.Naming;

namespace DarkAmbientRadio.Core.Tests;

public class TitleCaseNormalizerTests
{
    [Theory]
    [InlineData("ETERNAL VOID", "Eternal Void")]
    [InlineData("eternal void", "Eternal Void")]
    [InlineData("Eternal Void", "Eternal Void")]
    public void Normalize_FixesAllCapsAndAllLower(string input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));

    [Theory]
    [InlineData("THE HALLS OF THE DEAD", "The Halls of the Dead")]
    [InlineData("a wind from the north", "A Wind from the North")]
    public void Normalize_KeepsMinorWordsLowercaseInsideTheString(string input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));

    [Theory]
    [InlineData("THE", "The")]                    // single word is both first and last
    [InlineData("SONGS OF", "Songs Of")]          // trailing minor word stays capitalised
    public void Normalize_AlwaysCapitalisesFirstAndLastWord(string input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));

    [Theory]
    [InlineData("MONOLITH PART II", "Monolith Part II")]
    [InlineData("CHAPTER IV: DESCENT", "Chapter IV: Descent")]
    public void Normalize_PreservesRomanNumerals(string input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));

    [Theory]
    [InlineData("McCoy Tyner", "McCoy Tyner")]
    [InlineData("DiN Records", "DiN Records")]
    public void Normalize_LeavesDeliberateMixedCaseAlone(string input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));

    [Theory]
    [InlineData("DEEP  SPACE", "Deep  Space")]         // double space preserved
    [InlineData("VOID/ECHO", "Void/Echo")]
    [InlineData("NORTH-WEST PASSAGE", "North-West Passage")]
    public void Normalize_PreservesSeparatorsAndPunctuation(string input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "")]
    [InlineData("2024", "2024")]
    public void Normalize_HandlesEmptyAndWordlessInput(string? input, string expected)
        => Assert.Equal(expected, TitleCaseNormalizer.Normalize(input));
}
