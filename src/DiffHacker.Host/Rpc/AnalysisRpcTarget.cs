using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Settings;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// The analysis surface: run one, read the stored one back, switch which grouping it is read in, or
/// mark part of it reviewed — and, around those, the library of earlier runs, the trace of what one
/// did, and whether the working tree has moved since.
/// <para>
/// <b>Which analysis</b> is always the caller's to say. Every read and write takes an optional
/// <c>analysisId</c> and falls back to the latest only when it is absent, so a run reopened from the
/// library is regrouped and marked as itself rather than quietly redirected to the newest one. An id
/// is only honoured for the repository it belongs to.
/// </para>
/// <para>
/// <c>get</c>, <c>run</c> and <c>setGrouping</c> return the whole <see cref="AnalysisView"/> rather
/// than a delta, so the renderer replaces its state instead of merging — the same arrangement
/// <see cref="ProfileRpcTarget"/> uses, and for the same reason: an analysis is one artifact and half
/// of a new one on top of half of an old one is not any analysis at all.
/// </para>
/// <para>
/// <c>setGrouping</c> spends nothing, and that is the requirement rather than a happy accident: both
/// groupings came out of the run that was already paid for, so switching reads the stored document a
/// second way. It never touches the runner, and there is no path from it to one.
/// </para>
/// <para>
/// <c>setReviewed</c> is the exception, and returns only <see cref="ReviewedState"/>. It keeps the
/// convention — the whole of what changed, never a patch to it — while declining to send three
/// hundred nodes and every explanation on them back across the bridge each time a checkbox moves.
/// </para>
/// <para>
/// <c>list</c>, <c>trace</c>, <c>checkFreshness</c> and <c>delete</c> reach the store and the git
/// layer and nothing else. None of them has a path to the runner: reopening, inspecting and checking
/// an analysis spend nothing.
/// </para>
/// <para>
/// There is no cancel method. A run is stopped through <c>$/cancelRequest</c>, which StreamJsonRpc
/// answers by cancelling the token this method already takes — an explicit RPC would be a second
/// way to do the same thing, and the second way is the one that gets out of step.
/// </para>
/// </summary>
public sealed partial class AnalysisRpcTarget(
    IAnalysisStore store,
    IAnalysisRunner runner,
    IAppSettingStore settings,
    AnalysisDefaults defaults,
    RunEventNotifier runEvents,
    AnalysisFreshnessChecker freshness,
    ILogger<AnalysisRpcTarget> logger)
{
    /// <summary>
    /// The grouping last chosen anywhere, which is what an analysis that has none of its own opens
    /// in. Application-wide rather than per repository: it is a habit about how someone reads, not a
    /// fact about a project.
    /// </summary>
    private const string DefaultGroupingKey = "analysis.grouping.default";

    [JsonRpcMethod("analysis.get")]
    public async Task<AnalysisView> GetAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Reading never runs anything. That is the whole promise of persisting the result: the
        // conversation happened once and was paid for once — and the reason reopening an earlier
        // run from the library can be instant.
        Analysis stored;

        if (request.AnalysisId is null)
        {
            var latest = await store
                .GetLatestAsync(request.RepositoryPath, cancellationToken)
                .ConfigureAwait(false);

            if (latest is null)
            {
                return AnalysisWire.Empty(request.RepositoryPath);
            }

            stored = latest;
        }
        else
        {
            stored = await ResolveStoredAsync(request.RepositoryPath, request.AnalysisId, cancellationToken)
                .ConfigureAwait(false);
        }

        var grouping = await ResolveGroupingAsync(stored, cancellationToken).ConfigureAwait(false);
        var isLatest = await IsLatestAsync(stored, cancellationToken).ConfigureAwait(false);

        return AnalysisWire.ToWire(stored, grouping, isLatest);
    }

    /// <summary>
    /// Runs an analysis of the working tree. Each part the request names overrides the default for
    /// this run only; nothing about the request is remembered. The defaults change through
    /// <c>analysis.saveDefaults</c> and nowhere else, so trimming one expensive re-run never quietly
    /// trims the ones after it.
    /// </summary>
    [JsonRpcMethod("analysis.run")]
    public async Task<AnalysisView> RunAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = await defaults
            .ResolveAsync(AnalysisWire.OverridesOf(request), cancellationToken)
            .ConfigureAwait(false);

        AnalysisRunResult result;

        try
        {
            result = await runner
                .RunAsync(
                    request.RepositoryPath,
                    options,
                    runEvents,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitClientException ex)
        {
            throw GitFailure(ex, request.RepositoryPath);
        }
        catch (LlmConfigurationException ex)
        {
            throw RpcErrors.Failure(ex.FailureCode, ex.Message);
        }

        if (result.Analysis is null)
        {
            throw Failure(result);
        }

        // A fresh run has no remembered grouping of its own, so it opens in whichever the reviewer
        // was last reading — clamped to what this answer actually holds.
        var grouping = await ResolveGroupingAsync(result.Analysis, cancellationToken).ConfigureAwait(false);

        // A run that has just been stored is the latest by construction.
        return AnalysisWire.ToWire(result.Analysis, grouping, isLatest: true);
    }

    /// <summary>
    /// What a run asks for when the renderer does not say: the defaults set in Settings. The run
    /// popover starts from these, and so does a run request that leaves a part out.
    /// </summary>
    [JsonRpcMethod("analysis.getDefaults")]
    public async Task<AnalysisOptions> GetDefaultsAsync(CancellationToken cancellationToken) =>
        AnalysisWire.ToWire(await defaults.GetAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Replaces the defaults, and answers with them as they now read back.</summary>
    [JsonRpcMethod("analysis.saveDefaults")]
    public async Task<AnalysisOptions> SaveDefaultsAsync(AnalysisOptions request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var saved = await defaults
            .SaveAsync(AnalysisWire.FromWire(request), cancellationToken)
            .ConfigureAwait(false);

        DefaultsSaved(
            logger,
            saved.ChangeClusters,
            saved.ImplementationGroups,
            saved.Risks,
            saved.NodeExplanations,
            saved.EdgeExplanations,
            saved.ContainerExplanations,
            AnalysisVerbosityNames.Of(saved.Verbosity));

        return AnalysisWire.ToWire(saved);
    }

    /// <summary>
    /// Shows the stored analysis in the other grouping. Requirement 2: this cannot re-run anything,
    /// and the only reason it can be instant is that the run produced both.
    /// </summary>
    [JsonRpcMethod("analysis.setGrouping")]
    public async Task<AnalysisView> SetGroupingAsync(
        SetGroupingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await ResolveStoredAsync(request.RepositoryPath, request.AnalysisId, cancellationToken)
            .ConfigureAwait(false);

        var grouping = AnalysisWire.FromWire(request.Grouping);

        // Refused rather than quietly substituted. An analysis produced before groupings existed, or
        // by a run that was told not to bother with the second one, genuinely does not have it, and
        // showing dependency flow while the control reads "change clusters" would be a lie the
        // reviewer has no way to see through.
        if (!stored.AvailableGroupings.Contains(grouping))
        {
            throw RpcErrors.Failure(
                "analysis_grouping_unavailable",
                $"Analysis {stored.Id} does not hold the '{AnalysisGroupingNames.Of(grouping)}' "
                    + "grouping. Re-run the analysis to produce it.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grouping"] = AnalysisGroupingNames.Of(grouping),
                });
        }

        await store.SetGroupingAsync(stored.Id, grouping, cancellationToken).ConfigureAwait(false);

        await settings
            .SetAsync(DefaultGroupingKey, AnalysisGroupingNames.Of(grouping), cancellationToken)
            .ConfigureAwait(false);

        var isLatest = await IsLatestAsync(stored, cancellationToken).ConfigureAwait(false);

        return AnalysisWire.ToWire(stored with { Grouping = grouping }, grouping, isLatest);
    }

    /// <summary>
    /// The library: every stored run of the repository, most recent first. Reads no document, so it
    /// is cheap enough to ask for whenever the screen opens.
    /// </summary>
    [JsonRpcMethod("analysis.list")]
    public async Task<AnalysisLibrary> ListAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await LibraryAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Every tool call one run made, in order, with how large each answer was. Its own call rather
    /// than part of the view, which travels on every read and every grouping switch.
    /// </summary>
    [JsonRpcMethod("analysis.trace")]
    public async Task<AnalysisTrace> TraceAsync(AnalysisRefRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await ResolveStoredAsync(request.RepositoryPath, request.AnalysisId, cancellationToken)
            .ConfigureAwait(false);

        return AnalysisWire.ToTrace(stored);
    }

    /// <summary>
    /// Whether the working tree still is the change this analysis describes. Asked by the renderer
    /// after the analysis is already on screen, so it can take as long as reading the changeset
    /// takes without making reopening anything but instant.
    /// </summary>
    [JsonRpcMethod("analysis.checkFreshness")]
    public async Task<AnalysisFreshness> CheckFreshnessAsync(
        AnalysisRefRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await ResolveStoredAsync(request.RepositoryPath, request.AnalysisId, cancellationToken)
            .ConfigureAwait(false);

        AnalysisFreshnessReport report;

        try
        {
            report = await freshness.CheckAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        catch (GitClientException ex)
        {
            throw GitFailure(ex, request.RepositoryPath);
        }

        FreshnessChecked(
            logger,
            stored.Id,
            report.IsStale,
            report.HeadMoved,
            report.Modified.Count,
            report.Added.Count,
            report.Removed.Count,
            report.Basis);

        return AnalysisWire.ToWire(report);
    }

    /// <summary>
    /// Forgets one stored run, and answers with the library as it now stands. Resolved through the
    /// repository first, so an id from another repository cannot be deleted through this one.
    /// </summary>
    [JsonRpcMethod("analysis.delete")]
    public async Task<AnalysisLibrary> DeleteAsync(AnalysisRefRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await ResolveStoredAsync(request.RepositoryPath, request.AnalysisId, cancellationToken)
            .ConfigureAwait(false);

        await store.DeleteOneAsync(stored.Id, cancellationToken).ConfigureAwait(false);
        AnalysisDeleted(logger, stored.Id);

        return await LibraryAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AnalysisLibrary> LibraryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var summaries = await store
            .ListSummariesAsync(repositoryPath, cancellationToken)
            .ConfigureAwait(false);

        return AnalysisWire.ToLibrary(repositoryPath, summaries);
    }

    /// <summary>
    /// The analysis a call is about: the one named, when it belongs to this repository, or the latest
    /// when none is named. Anything else is <c>analysis_not_found</c> — an id from another repository
    /// included, because reading one repository's analysis through another's path is exactly the
    /// kind of stale selection the path on every request exists to prevent.
    /// </summary>
    private async Task<Analysis> ResolveStoredAsync(
        string repositoryPath,
        string? analysisId,
        CancellationToken cancellationToken)
    {
        var stored = analysisId is null
            ? await store.GetLatestAsync(repositoryPath, cancellationToken).ConfigureAwait(false)
            : await store.FindAsync(analysisId, cancellationToken).ConfigureAwait(false);

        if (stored is null || !RepositoryPaths.PathsEqual(stored.RepositoryPath, repositoryPath))
        {
            var args = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = repositoryPath };

            if (analysisId is not null)
            {
                args["analysisId"] = analysisId;
            }

            throw RpcErrors.Failure(
                "analysis_not_found",
                analysisId is null
                    ? $"No analysis is stored for '{repositoryPath}'."
                    : $"No analysis '{analysisId}' is stored for '{repositoryPath}'.",
                args);
        }

        return stored;
    }

    /// <summary>Whether <paramref name="analysis"/> is its repository's most recent run.</summary>
    private async Task<bool> IsLatestAsync(Analysis analysis, CancellationToken cancellationToken)
    {
        var summaries = await store
            .ListSummariesAsync(analysis.RepositoryPath, cancellationToken)
            .ConfigureAwait(false);

        return summaries.Count > 0 && string.Equals(summaries[0].Id, analysis.Id, StringComparison.Ordinal);
    }

    private static LocalRpcException GitFailure(GitClientException ex, string repositoryPath) =>
        RpcErrors.Failure(
            ex.Failure is GitClientFailure.GitUnavailable ? "git_not_found" : "analysis_repository_unreadable",
            ex.Message,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = repositoryPath });

    /// <summary>
    /// Which grouping to show: the one remembered against this analysis, else the one last chosen
    /// anywhere, else dependency flow — and never one this analysis does not hold.
    /// </summary>
    private async Task<AnalysisGrouping> ResolveGroupingAsync(
        Analysis analysis,
        CancellationToken cancellationToken)
    {
        var stored = analysis.Grouping;

        if (stored is null)
        {
            var setting = await settings.GetAsync(DefaultGroupingKey, cancellationToken).ConfigureAwait(false);
            stored = AnalysisGroupingNames.Parse(setting);
        }

        return stored is { } grouping && analysis.AvailableGroupings.Contains(grouping)
            ? grouping
            : AnalysisGrouping.DependencyFlow;
    }

    [JsonRpcMethod("analysis.setReviewed")]
    public async Task<ReviewedState> SetReviewedAsync(
        SetNodesReviewedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await ResolveStoredAsync(request.RepositoryPath, request.AnalysisId, cancellationToken)
            .ConfigureAwait(false);

        // Checked against the document here rather than inside the store, because whether an id
        // belongs to this analysis is a question about the model's answer, and this is the layer
        // holding it. A single unknown id rejects the whole call: a partly-applied mark is a state
        // the reviewer would have to discover by counting.
        var known = stored.Document.Nodes
            .Select(static node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var nodeId in request.NodeIds)
        {
            if (!known.Contains(nodeId))
            {
                throw RpcErrors.Failure(
                    "analysis_node_not_found",
                    $"'{nodeId}' is not a node of analysis {stored.Id}.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = nodeId });
            }
        }

        var marks = await store
            .SetNodesReviewedAsync(stored.Id, request.NodeIds, request.Reviewed, cancellationToken)
            .ConfigureAwait(false);

        return new ReviewedState(analysisId: stored.Id, reviewedNodeIds: marks);
    }

    /// <summary>
    /// Turns a failed run into the error the renderer renders.
    /// <para>
    /// A validation failure carries its diagnostics in the detail, because requirement 4 is that a
    /// run which exhausts its repairs fails <i>loudly</i> and shows what was wrong. "The analysis
    /// could not be validated" on its own would be the quiet failure that requirement exists to
    /// forbid.
    /// </para>
    /// </summary>
    private LocalRpcException Failure(AnalysisRunResult result)
    {
        var code = result.FailureCode ?? LlmFailures.UnexpectedResponse;
        var args = new Dictionary<string, string>(StringComparer.Ordinal);

        var detail = result.Diagnostics.Count > 0
            ? string.Join("\n", result.Diagnostics.Take(10).Select(static d => d.Message))
            : result.ProviderMessage;

        if (detail is { Length: > 0 })
        {
            args["detail"] = detail;
        }

        RunFailed(logger, code, result.Diagnostics.Count, result.Usage.TotalTokens);

        return RpcErrors.Failure(
            code,
            result.ProviderMessage ?? "The analysis did not complete.",
            args.Count == 0 ? null : args);
    }

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "An analysis run failed with {FailureCode} after {Tokens} token(s); "
            + "{DiagnosticCount} diagnostic(s) reported.")]
    private static partial void RunFailed(
        ILogger logger,
        string failureCode,
        int diagnosticCount,
        long tokens);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Information,
        Message = "Checked analysis {AnalysisId} against the working tree: stale={IsStale}, "
            + "HEAD moved={HeadMoved}, {Modified} modified, {Added} added, {Removed} removed, by {Basis}.")]
    private static partial void FreshnessChecked(
        ILogger logger,
        string analysisId,
        bool isStale,
        bool headMoved,
        int modified,
        int added,
        int removed,
        Core.Analyses.AnalysisFreshnessBasis basis);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Information,
        Message = "Deleted analysis {AnalysisId} from the library.")]
    private static partial void AnalysisDeleted(ILogger logger, string analysisId);

    [LoggerMessage(
        EventId = 7104,
        Level = LogLevel.Information,
        Message = "Saved analysis defaults: change clusters={ChangeClusters}, implementation groups="
            + "{ImplementationGroups}, risks={Risks}, node explanations={NodeExplanations}, edge "
            + "explanations={EdgeExplanations}, cluster explanations={ContainerExplanations}, "
            + "verbosity={Verbosity}.")]
    private static partial void DefaultsSaved(
        ILogger logger,
        bool changeClusters,
        bool implementationGroups,
        bool risks,
        bool nodeExplanations,
        bool edgeExplanations,
        bool containerExplanations,
        string verbosity);
}
