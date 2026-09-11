namespace DiffHacker.Core.Analyses;

/// <summary>
/// How long each written field should be, in characters, at each <see cref="AnalysisVerbosity"/>.
/// <para>
/// One set of numbers with two readers: <see cref="AnalysisPrompt"/> asks for them, and
/// <see cref="AnalysisValidator"/> notices when a field runs to twice one. Kept here rather than
/// spelled out twice, because two copies of a number is one copy waiting to disagree.
/// </para>
/// <para>
/// <b>Deliberately not <c>maxLength</c> in the schema.</b> The result schema goes verbatim into
/// the provider's <c>json_schema</c> response format, so a length constraint there is enforced by
/// the provider — and a violation costs a full repair round on a document that may be three
/// hundred nodes long. A model that writes seventy characters where sixty were asked for has done
/// nothing worth spending another request on. So these are asked for in the prompt and reported as a
/// warning when badly overshot.
/// </para>
/// <para>
/// Titles do not scale with verbosity. They are labels on a box the renderer clamps to
/// <see cref="NodeTitle"/> characters whatever the model wrote, so a "detailed" title would only be a
/// longer truncated one.
/// </para>
/// </summary>
public static class AnalysisFieldBudgets
{
    /// <summary>A label on a node box, not a sentence. The same at every verbosity.</summary>
    public const int NodeTitle = 60;

    /// <summary>A container heading. The same at every verbosity.</summary>
    public const int ContainerTitle = 50;

    /// <summary>
    /// How far past its budget a field goes before it is worth saying so. Twice, because the
    /// budgets are what fits comfortably rather than a hard edge, and a diagnostic raised on every
    /// slight overshoot is one nobody reads.
    /// </summary>
    public const int OvershootFactor = 2;

    private static readonly AnalysisFieldBudgetSet Brief = new()
    {
        NodeProse = 120,
        ContainerSummary = 120,
        ContainerExplanation = 300,
        OverallSummary = 400,
        Risk = 140,
    };

    /// <summary>The lengths every analysis before 1.15 was asked for.</summary>
    private static readonly AnalysisFieldBudgetSet Medium = new()
    {
        NodeProse = 240,
        ContainerSummary = 200,
        ContainerExplanation = 600,
        OverallSummary = 800,
        Risk = 200,
    };

    private static readonly AnalysisFieldBudgetSet Detailed = new()
    {
        NodeProse = 480,
        ContainerSummary = 300,
        ContainerExplanation = 1200,
        OverallSummary = 1600,
        Risk = 300,
    };

    /// <summary>The budgets for one verbosity.</summary>
    public static AnalysisFieldBudgetSet For(AnalysisVerbosity verbosity) => verbosity switch
    {
        AnalysisVerbosity.Brief => Brief,
        AnalysisVerbosity.Medium => Medium,
        AnalysisVerbosity.Detailed => Detailed,
        _ => throw new ArgumentOutOfRangeException(nameof(verbosity), verbosity, "Unnamed verbosity."),
    };

    /// <summary>Whether <paramref name="text"/> is long enough past <paramref name="budget"/> to report.</summary>
    public static bool IsOverlong(string? text, int budget) =>
        text is not null && text.Length > budget * OvershootFactor;
}

/// <summary>The lengths that scale with verbosity, for one verbosity.</summary>
public sealed record AnalysisFieldBudgetSet
{
    /// <summary>A node's title. Fixed; here so a caller reads every budget from one place.</summary>
    public int NodeTitle { get; } = AnalysisFieldBudgets.NodeTitle;

    /// <summary>A container's title. Fixed, like <see cref="NodeTitle"/>.</summary>
    public int ContainerTitle { get; } = AnalysisFieldBudgets.ContainerTitle;

    /// <summary>Each of the four prose fields on a node.</summary>
    public required int NodeProse { get; init; }

    public required int ContainerSummary { get; init; }

    public required int ContainerExplanation { get; init; }

    public required int OverallSummary { get; init; }

    /// <summary>One risk, not the list.</summary>
    public required int Risk { get; init; }
}
