using System.Text.RegularExpressions;

namespace DarkAmbientRadio.Core.Sources;

/// <summary>
/// Validates and normalises Bandcamp review codes. Default shape is "12ab-3cd4"
/// (two groups of four lowercase alphanumerics), but the pattern is configurable.
/// </summary>
public sealed class ReviewCodeValidator
{
    public const string DefaultPattern = "^[0-9a-z]{4}-[0-9a-z]{4}$";

    private readonly Regex _regex;

    public ReviewCodeValidator(string? pattern = null)
    {
        _regex = new Regex(pattern ?? DefaultPattern, RegexOptions.Compiled);
    }

    /// <summary>
    /// Trims and lower-cases the candidate, then checks it against the pattern.
    /// Returns the normalised code via <paramref name="normalized"/> when valid.
    /// </summary>
    public bool TryNormalize(string? candidate, out string normalized)
    {
        normalized = (candidate ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length > 0 && _regex.IsMatch(normalized);
    }

    public bool IsValid(string? candidate) => TryNormalize(candidate, out _);
}
