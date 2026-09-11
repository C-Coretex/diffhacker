using DiffHacker.Core.Llm;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// Runs one analysis: changeset in, validated and persisted graph out.
/// </summary>
public interface IAnalysisRunner
{
    /// <summary>
    /// Analyses the working tree of <paramref name="repositoryPath"/> against HEAD.
    /// <para>
    /// Only cancellation throws. Every other ending — no provider configured, a revoked key, a
    /// context overflow, a result that would not validate — comes back as a result carrying the
    /// code and what the attempt cost, because the caller needs all of that at once.
    /// </para>
    /// </summary>
    Task<AnalysisRunResult> RunAsync(
        string repositoryPath,
        AnalysisRunOptions options,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// What this run should ask the model for, as distinct from what the analysis is <i>of</i>.
/// <para>
/// A record rather than a parameter because it is the shape the answer to "which parts of the
/// analysis do I actually want to pay for?" takes. Every part that is switched off is taken out of
/// the prompt and out of the response schema — not asked for and discarded — because both reach the
/// model on every turn, and the schema twice. The prompt then says plainly that the work is not
/// wanted, so the model does not do it anyway in a field that is still there.
/// </para>
/// <para>
/// Value equality matters: <see cref="AnalysisResponseSchema"/> caches a schema per distinct set of
/// options, and the record is stored beside the document so the renderer can tell "not asked for"
/// from "nothing to say".
/// </para>
/// </summary>
public sealed record AnalysisRunOptions
{
    /// <summary>
    /// Whether to ask for the change-clusters grouping as well as dependency flow. False takes it out
    /// of the prompt and out of the response schema, so a reviewer who never switches is not charged
    /// for it on every turn.
    /// </summary>
    public bool ChangeClusters { get; init; } = true;

    /// <summary>
    /// Whether to ask which abstractions were changed alongside their implementations. False takes
    /// the declaration out of the prompt and the response schema for the same reason, and the stored
    /// document then carries no such field at all.
    /// </summary>
    public bool ImplementationGroups { get; init; } = true;

    /// <summary>
    /// Whether to ask for risks — overall, per container, per node and per edge, and the risky state.
    /// False removes every one of those fields, and the prompt tells the model not to assess risk at
    /// all rather than letting it fold warnings into the prose.
    /// </summary>
    public bool Risks { get; init; } = true;

    /// <summary>
    /// Whether each node gets its four prose fields — what changed, why, what it affects, notes.
    /// False leaves a node its title, its place and its importance.
    /// </summary>
    public bool NodeExplanations { get; init; } = true;

    /// <summary>Whether each edge gets an explanation. False leaves it its two ends and its kind.</summary>
    public bool EdgeExplanations { get; init; } = true;

    /// <summary>Whether each container gets a summary and an explanation. False leaves it its title.</summary>
    public bool ContainerExplanations { get; init; } = true;

    /// <summary>How long the prose that is asked for should be.</summary>
    public AnalysisVerbosity Verbosity { get; init; } = AnalysisVerbosity.Brief;

    /// <summary>
    /// Every part, at the default verbosity — what a run does unless the reviewer said otherwise.
    /// </summary>
    public static AnalysisRunOptions Default { get; } = new();

    /// <summary>
    /// What an analysis written before 1.15 was asked for, as far as it can be told: every part it
    /// could have had, at the lengths the prompt then asked for. The two groupings and implementation
    /// groups are read from the document instead, which does know.
    /// </summary>
    public static AnalysisRunOptions Legacy { get; } = new() { Verbosity = AnalysisVerbosity.Medium };
}

/// <summary>The outcome of one analysis run, complete with what it cost.</summary>
public sealed record AnalysisRunResult
{
    /// <summary>The stored analysis. Null unless the run completed.</summary>
    public Analysis? Analysis { get; init; }

    /// <summary>An <see cref="AnalysisFailures"/> or <see cref="LlmFailures"/> code, or null.</summary>
    public string? FailureCode { get; init; }

    /// <summary>The provider's own wording, or our own explanation of a validation failure.</summary>
    public string? ProviderMessage { get; init; }

    /// <summary>
    /// The errors that stopped the result being accepted, when that is what happened. Requirement 4
    /// says a run that exhausts its repairs fails loudly and shows the user what was wrong; this is
    /// what it shows them.
    /// </summary>
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>What the attempt spent, whether or not it produced anything.</summary>
    public LlmUsage Usage { get; init; }

    public TimeSpan Duration { get; init; }

    public bool Succeeded => Analysis is not null;
}
