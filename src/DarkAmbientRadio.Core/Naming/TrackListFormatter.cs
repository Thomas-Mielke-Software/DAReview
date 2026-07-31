using System.Text;

namespace DarkAmbientRadio.Core.Naming;

/// <summary>
/// Builds the folder-name suffix that documents which tracks made it onto air.
/// Existing library convention: square brackets, e.g. "[OHNE TRACK 2, 3 und 5]"
/// or "[NUR TRACK 1 UND 4]". Whichever of the two variants is the shorter string wins.
/// </summary>
public static class TrackListFormatter
{
    /// <summary>
    /// Renders an ascending list of track numbers in German reading style:
    /// single -> "3", pair -> "1 und 4", more -> "2, 3 und 5".
    /// </summary>
    public static string FormatNumberList(IEnumerable<int> numbers, string connector)
    {
        var ordered = numbers.Distinct().OrderBy(n => n).ToList();
        if (ordered.Count == 0)
            return string.Empty;
        if (ordered.Count == 1)
            return ordered[0].ToString();

        var sb = new StringBuilder();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(ordered[i]);
        }
        sb.Append(' ').Append(connector).Append(' ').Append(ordered[^1]);
        return sb.ToString();
    }

    /// <summary>
    /// Produces the suffix (including a leading space) to append to the album folder name.
    /// Returns an empty string when nothing was rejected. When both variants are equally
    /// long, the "OHNE" (without) variant is preferred.
    /// </summary>
    /// <param name="allTrackNumbers">Every track number present in the album.</param>
    /// <param name="rejectedTrackNumbers">The subset that was rejected.</param>
    /// <param name="connectorOhne">Connector word for the OHNE variant (default "und").</param>
    /// <param name="connectorNur">Connector word for the NUR variant (default "UND").</param>
    public static string BuildSuffix(
        IEnumerable<int> allTrackNumbers,
        IEnumerable<int> rejectedTrackNumbers,
        string connectorOhne = "und",
        string connectorNur = "UND")
    {
        var all = allTrackNumbers.Distinct().OrderBy(n => n).ToList();
        var rejected = rejectedTrackNumbers.Distinct().Where(all.Contains).OrderBy(n => n).ToList();
        if (rejected.Count == 0)
            return string.Empty;

        var approved = all.Where(n => !rejected.Contains(n)).ToList();
        var ohne = $"[OHNE TRACK {FormatNumberList(rejected, connectorOhne)}]";

        // If nothing is approved there is no "NUR" variant to compare against.
        if (approved.Count == 0)
            return " " + ohne;

        var nur = $"[NUR TRACK {FormatNumberList(approved, connectorNur)}]";
        var chosen = nur.Length < ohne.Length ? nur : ohne;
        return " " + chosen;
    }
}
