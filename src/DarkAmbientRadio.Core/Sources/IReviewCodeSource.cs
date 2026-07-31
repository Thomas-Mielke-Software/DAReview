namespace DarkAmbientRadio.Core.Sources;

/// <summary>
/// Supplies review codes to the acquisition pipeline. The current implementation
/// reads the Windows clipboard; a Thunderbird e-mail source can be added later
/// without touching the rest of the workflow.
/// </summary>
public interface IReviewCodeSource
{
    /// <summary>Human-readable name of the source (for UI/logging).</summary>
    string Name { get; }

    /// <summary>
    /// Returns the current review code, or null when none is available / valid.
    /// </summary>
    string? TryGetCode();
}
