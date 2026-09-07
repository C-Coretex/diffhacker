using DiffHacker.Core.Llm;

namespace DiffHacker.Core.Knowledge;

/// <summary>How a profile run ended.</summary>
public enum ProfileRunOutcome
{
    Completed,

    /// <summary>The user stopped it. Partial cost is real and is reported; nothing is stored.</summary>
    Cancelled,

    /// <summary>Anything else, including a document that would not fit the size budget.</summary>
    Failed,
}

/// <summary>
/// One attempt at building a profile, kept after the fact.
/// <para>
/// The ordered tool trace is the point. "Confirm the run read the README before it started
/// grepping code" is a verification step for this iteration, and it is only answerable if the
/// order survives the run — reading it back out of <c>log.txt</c> would work once, on the machine
/// that produced it. Iteration 13's tool-call inspector reads the same records.
/// </para>
/// </summary>
public sealed record ProfileRun
{
    public required string Id { get; init; }

    public required string RepositoryPath { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? FinishedAtUtc { get; init; }

    public required ProfileRunOutcome Outcome { get; init; }

    /// <summary>An <see cref="LlmFailures"/>-style code when the run did not complete.</summary>
    public string? FailureCode { get; init; }

    public required string ProviderDisplayName { get; init; }

    public required string Model { get; init; }

    public LlmUsage Usage { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Every tool call, in the order the model made them.</summary>
    public IReadOnlyList<LlmToolCallRecord> ToolCalls { get; init; } = [];

    /// <summary>What the model said it was doing, in order.</summary>
    public IReadOnlyList<string> ProgressMessages { get; init; } = [];
}
