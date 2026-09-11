using DiffHacker.Core.Llm;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// One stored run as the library lists it: the facts a reviewer chooses between, and nothing of
/// the model's answer. Read without deserialising the document, so listing twenty runs costs twenty
/// rows rather than twenty graphs.
/// </summary>
public sealed record AnalysisSummary
{
    public required string Id { get; init; }

    public required string RepositoryPath { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string? HeadCommit { get; init; }

    public required string ProviderDisplayName { get; init; }

    public required string Model { get; init; }

    public LlmUsage Usage { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Changed files in the changeset the run analysed.</summary>
    public int FileCount { get; init; }

    public int LinesAdded { get; init; }

    public int LinesRemoved { get; init; }

    public int NodeCount { get; init; }

    /// <summary>Containers in the dependency-flow grouping, the one statistics are recorded for.</summary>
    public int ContainerCount { get; init; }

    public int ToolCallCount { get; init; }
}

/// <summary>How much analysis history is kept.</summary>
public static class AnalysisLibraryPolicy
{
    /// <summary>
    /// Analyses kept per repository. Generous, because re-running one costs money and the point of
    /// storing them is not having to; bounded, because a five-hundred-node document is not small
    /// and an unbounded history of them would grow without anyone deciding it should. One number
    /// shared by the store that prunes and the library that tells the reviewer it does.
    /// </summary>
    public const int RetentionLimit = 20;
}
