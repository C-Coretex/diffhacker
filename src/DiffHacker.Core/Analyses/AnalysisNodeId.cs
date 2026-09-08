namespace DiffHacker.Core.Analyses;

/// <summary>
/// The shape of a node identifier, and the one place that decides whether one is well formed.
/// <para>
/// §0.6 settles that node identity is derived from the file path so it survives a re-run: the
/// reviewed-state a later iteration keys off a node id would otherwise reset every time the model
/// phrased something differently. For the usual one-node-per-file case the id <i>is</i> the path,
/// which makes stability a property of the shape rather than a hope about the model. Where a file
/// genuinely splits, the path is still the whole prefix and only the suffix is the model's choice.
/// </para>
/// </summary>
public static class AnalysisNodeId
{
    /// <summary>Separates the path from the disambiguator.</summary>
    public const char Separator = '#';

    /// <summary>
    /// Whether <paramref name="id"/> is derived from <paramref name="filePath"/>: either exactly
    /// the path, or the path followed by '#' and a lowercase-and-hyphens slug.
    /// </summary>
    public static bool IsDerivedFrom(string? id, string? filePath)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        if (string.Equals(id, filePath, StringComparison.Ordinal))
        {
            return true;
        }

        if (!id.StartsWith(filePath, StringComparison.Ordinal) ||
            id.Length <= filePath.Length ||
            id[filePath.Length] != Separator)
        {
            return false;
        }

        return IsSlug(id.AsSpan(filePath.Length + 1));
    }

    /// <summary>The expected id for a file that yields exactly one node.</summary>
    public static string ForWholeFile(string filePath) => filePath;

    /// <summary>What a well-formed id looks like, for the diagnostic that rejects one.</summary>
    public static string Describe(string filePath) =>
        $"'{filePath}' or '{filePath}{Separator}<slug>'";

    private static bool IsSlug(ReadOnlySpan<char> value)
    {
        if (value.Length == 0 || !char.IsAsciiLetterLower(value[0]) && !char.IsAsciiDigit(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
