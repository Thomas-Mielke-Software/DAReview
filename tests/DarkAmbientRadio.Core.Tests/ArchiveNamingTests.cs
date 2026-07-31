using DarkAmbientRadio.Core.Naming;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

public class ArchiveNamingTests
{
    [Theory]
    [InlineData("Ager Sonus - Necropolis.zip", "Ager Sonus - Necropolis")]
    [InlineData("Ager Sonus - Necropolis.ZIP", "Ager Sonus - Necropolis")]
    [InlineData("Some Album (pre-order).zip", "Some Album")]
    [InlineData("Some Album (pre-order).ZIP", "Some Album")]
    [InlineData("Plain Folder Name", "Plain Folder Name")]
    [InlineData(@"C:\Users\me\Downloads\Artist - Title.zip", "Artist - Title")]
    public void DeriveFolderName_strips_zip_and_preorder(string input, string expected)
        => Assert.Equal(expected, ArchiveNaming.DeriveFolderName(input));
}
