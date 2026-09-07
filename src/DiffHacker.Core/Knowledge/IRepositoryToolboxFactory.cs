using DiffHacker.Core.Llm;

namespace DiffHacker.Core.Knowledge;

/// <summary>
/// Opens the repository toolbox for one run.
/// <para>
/// The toolbox itself lives in <c>DiffHacker.Tools</c>, which references this project; an
/// orchestrator here cannot reference it back without a cycle. So the orchestration depends on
/// this interface and the toolbox implements it — the same arrangement <c>IGitClient</c> has with
/// <c>DiffHacker.Git</c>, and for the same reason.
/// </para>
/// <para>
/// Iteration 7 opens a toolbox the same way for the analysis run.
/// </para>
/// </summary>
public interface IRepositoryToolboxFactory
{
    /// <summary>
    /// Takes the repository snapshot and returns the tools a session may call.
    /// </summary>
    /// <param name="repositoryPath">Worktree root. Everything the tools can reach is under it.</param>
    /// <param name="excludedGlobs">
    /// Paths whose <i>content</i> is withheld. Matching files stay listed and flagged; only reads,
    /// diffs and search hits are refused, so nothing disappears from the changeset.
    /// </param>
    /// <param name="progress">
    /// Where <c>report_progress</c> goes for this run. Passed per run rather than taken from the
    /// container so the orchestrator can wrap the application's sink and keep a copy of what the
    /// model said for the stored trace.
    /// </param>
    Task<IRepositoryToolbox> OpenAsync(
        string repositoryPath,
        IReadOnlyList<string> excludedGlobs,
        Tools.IToolProgressSink progress,
        CancellationToken cancellationToken);
}

/// <summary>The tools one run may call, already bound to a repository.</summary>
public interface IRepositoryToolbox
{
    IReadOnlyList<LlmToolDefinition> Tools { get; }

    /// <summary>Files git can see in this repository — the denominator for drift.</summary>
    int VisibleFileCount { get; }
}
