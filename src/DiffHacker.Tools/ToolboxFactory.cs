using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Tools;

/// <summary>
/// The orchestration layer's way in to the toolbox.
/// <para>
/// <c>DiffHacker.Core</c> holds the run orchestrator and cannot reference this project without a
/// cycle, so it depends on <see cref="IRepositoryToolboxFactory"/> and this implements it. The
/// arrangement is the same one <c>IGitClient</c> has, and it means Iteration 7's analysis run gets
/// a toolbox exactly the way the profile run does, through one entry point that neither of them
/// can configure differently.
/// </para>
/// </summary>
public sealed class ToolboxFactory(IGitClient git, IProjectProfileSource profiles, ILoggerFactory loggerFactory)
    : IRepositoryToolboxFactory
{
    public async Task<IRepositoryToolbox> OpenAsync(
        string repositoryPath,
        IReadOnlyList<string> excludedGlobs,
        IToolProgressSink progress,
        CancellationToken cancellationToken)
    {
        var catalogue = await Toolbox.OpenAsync(
            new ToolboxOptions
            {
                Git = git,
                LoggerFactory = loggerFactory,
                Progress = progress,
                Profiles = profiles,
                WithheldGlobs = excludedGlobs,
            },
            repositoryPath,
            cancellationToken).ConfigureAwait(false);

        var visible = await git
            .ListFilesAsync(new FileListQuery(repositoryPath), cancellationToken)
            .ConfigureAwait(false);

        return new OpenToolbox(catalogue.LlmTools, visible.Count);
    }

    private sealed record OpenToolbox(IReadOnlyList<Core.Llm.LlmToolDefinition> Tools, int VisibleFileCount)
        : IRepositoryToolbox;
}
