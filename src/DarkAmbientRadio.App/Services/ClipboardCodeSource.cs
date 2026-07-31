using System.Windows;
using DarkAmbientRadio.Core.Sources;

namespace DarkAmbientRadio.App.Services;

/// <summary>Reads a review code from the Windows clipboard.</summary>
public sealed class ClipboardCodeSource : IReviewCodeSource
{
    private readonly ReviewCodeValidator _validator;

    public ClipboardCodeSource(ReviewCodeValidator validator) => _validator = validator;

    public string Name => "Zwischenablage";

    public string? TryGetCode()
    {
        if (!Clipboard.ContainsText())
            return null;

        return _validator.TryNormalize(Clipboard.GetText(), out var code) ? code : null;
    }
}
