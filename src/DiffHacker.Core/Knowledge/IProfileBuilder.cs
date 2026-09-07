using DiffHacker.Core.Llm;

namespace DiffHacker.Core.Knowledge;

/// <summary>
/// Runs the LLM over a whole repository and stores what it produces.
/// <para>
/// The first orchestrator in the application: everything before this iteration built halves of a
/// pipe that nothing pushed anything through.
/// </para>
/// </summary>
public interface IProfileBuilder
{
    /// <summary>
    /// Profiles <paramref name="repositoryPath"/> and, on success, replaces the stored generated
    /// document. The user's sections are untouched whatever happens.
    /// </summary>
    /// <param name="progress">
    /// Per-turn and per-tool-call events, forwarded live. Called on the running thread, so an
    /// implementation must not block in it.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the run. Throws <see cref="OperationCanceledException"/>, having first recorded what
    /// the abandoned run spent — a cancelled run costs real money and saying so is the point.
    /// Nothing is stored as a profile.
    /// </param>
    Task<ProfileBuildResult> BuildAsync(
        string repositoryPath,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken);
}

/// <summary>What one build attempt produced.</summary>
public sealed record ProfileBuildResult
{
    public required ProfileRunOutcome Outcome { get; init; }

    /// <summary>The stored profile, set only when the run completed.</summary>
    public ProjectProfile? Profile { get; init; }

    /// <summary>The run as it was recorded, including its cost and its tool trace.</summary>
    public required ProfileRun Run { get; init; }

    /// <summary>A stable code the host translates. Null exactly when the run completed.</summary>
    public string? FailureCode { get; init; }

    /// <summary>The provider's own wording, for the log and the detail line. Never the API key.</summary>
    public string? ProviderMessage { get; init; }

    public bool Succeeded => Outcome is ProfileRunOutcome.Completed;
}

/// <summary>
/// Failures that belong to profiling rather than to the LLM layer. Every value is a resource key
/// in the renderer's catalogue.
/// </summary>
public static class ProfileFailures
{
    /// <summary>No provider is configured, or none is marked active.</summary>
    public const string NoProvider = "profile_no_provider";

    /// <summary>
    /// The document would not fit the size budget, even after being handed back to be shortened.
    /// Nothing is stored: a profile silently truncated to fit would be a profile that ends
    /// mid-sentence on every future run.
    /// </summary>
    public const string OverBudget = "profile_over_budget";

    /// <summary>The model answered, but not with something that could be read as a profile.</summary>
    public const string UnreadableAnswer = "profile_unreadable_answer";
}
