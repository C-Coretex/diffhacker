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
/// analysis do I actually want to pay for?" takes, and there will be more of them than one.
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

    /// <summary>Both groupings — what a run does unless the reviewer said otherwise.</summary>
    public static AnalysisRunOptions Default { get; } = new();
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
