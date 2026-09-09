namespace DiffHacker.Core.Analyses;

/// <summary>
/// How long each written field should be, in characters.
/// <para>
/// One set of numbers with three readers: <see cref="AnalysisPrompt"/> asks for them,
/// <see cref="AnalysisValidator"/> notices when a field runs to twice one, and the renderer
/// truncates to them. Kept here rather than spelled out three times, because three copies of a
/// number is two copies waiting to disagree.
/// </para>
/// <para>
/// <b>Deliberately not <c>maxLength</c> in the schema.</b> The result schema goes verbatim into
/// the provider's <c>json_schema</c> response format, so a length constraint there is enforced by
/// the provider — and a violation costs a full repair round on a document that may be three
/// hundred nodes long. A model that writes seventy characters where sixty were asked for has done
/// nothing worth spending another request on. So these are asked for in the prompt, reported as a
/// warning when badly overshot, and truncated at the point of display.
/// </para>
/// </summary>
public static class AnalysisFieldBudgets
{
    /// <summary>A label on a node box, not a sentence.</summary>
    public const int NodeTitle = 60;

    /// <summary>Each of the four prose fields on a node.</summary>
    public const int NodeProse = 240;

    public const int ContainerTitle = 50;

    public const int ContainerSummary = 200;

    public const int ContainerExplanation = 600;

    public const int OverallSummary = 800;

    /// <summary>One risk, not the list.</summary>
    public const int Risk = 200;

    /// <summary>
    /// How far past its budget a field goes before it is worth saying so. Twice, because the
    /// budgets are what fits comfortably rather than a hard edge, and a diagnostic raised on every
    /// slight overshoot is one nobody reads.
    /// </summary>
    public const int OvershootFactor = 2;

    /// <summary>Whether <paramref name="text"/> is long enough past <paramref name="budget"/> to report.</summary>
    public static bool IsOverlong(string? text, int budget) =>
        text is not null && text.Length > budget * OvershootFactor;
}
