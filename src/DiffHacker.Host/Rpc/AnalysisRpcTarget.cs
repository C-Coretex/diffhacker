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
/// mark part of it reviewed.
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
/// There is no cancel method. A run is stopped through <c>$/cancelRequest</c>, which StreamJsonRpc
/// answers by cancelling the token this method already takes — an explicit RPC would be a second
/// way to do the same thing, and the second way is the one that gets out of step.
/// </para>
/// </summary>
public sealed partial class AnalysisRpcTarget(
    IAnalysisStore store,
    IAnalysisRunner runner,
    IAppSettingStore settings,
    RunEventNotifier runEvents,
    ILogger<AnalysisRpcTarget> logger)
{
    /// <summary>
    /// The grouping last chosen anywhere, which is what an analysis that has none of its own opens
    /// in. Application-wide rather than per repository: it is a habit about how someone reads, not a
    /// fact about a project.
    /// </summary>
    private const string DefaultGroupingKey = "analysis.grouping.default";

    /// <summary>Whether runs should ask for the second grouping. The remembered answer, overridable per run.</summary>
    private const string ProduceClustersKey = "analysis.grouping.clusters";

    [JsonRpcMethod("analysis.get")]
    public async Task<AnalysisView> GetAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await store
            .GetLatestAsync(request.RepositoryPath, cancellationToken)
            .ConfigureAwait(false);

        var produceClusters = await ProduceClustersAsync(null, cancellationToken).ConfigureAwait(false);

        // Reading never runs anything. That is the whole promise of persisting the result: the
        // conversation happened once and was paid for once.
        if (stored is null)
        {
            return AnalysisWire.Empty(request.RepositoryPath, produceClusters);
        }

        var grouping = await ResolveGroupingAsync(stored, cancellationToken).ConfigureAwait(false);

        return AnalysisWire.ToWire(stored, grouping, produceClusters);
    }

    [JsonRpcMethod("analysis.run")]
    public async Task<AnalysisView> RunAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var produceClusters = await ProduceClustersAsync(request.ChangeClusters, cancellationToken)
            .ConfigureAwait(false);

        // Remembered before the run rather than after it, so a run that is cancelled or fails still
        // leaves the control showing what the reviewer chose.
        await settings
            .SetAsync(ProduceClustersKey, produceClusters ? "true" : "false", cancellationToken)
            .ConfigureAwait(false);

        AnalysisRunResult result;

        try
        {
            result = await runner
                .RunAsync(
                    request.RepositoryPath,
                    new AnalysisRunOptions { ChangeClusters = produceClusters },
                    runEvents,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitClientException ex)
        {
            throw RpcErrors.Failure(
                ex.Failure is GitClientFailure.GitUnavailable ? "git_not_found" : "analysis_repository_unreadable",
                ex.Message,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.RepositoryPath });
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

        return AnalysisWire.ToWire(result.Analysis, grouping, produceClusters);
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

        var stored = await store
            .GetLatestAsync(request.RepositoryPath, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            throw RpcErrors.Failure(
                "analysis_not_found",
                $"No analysis is stored for '{request.RepositoryPath}'.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.RepositoryPath });
        }

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

        var produceClusters = await ProduceClustersAsync(null, cancellationToken).ConfigureAwait(false);

        return AnalysisWire.ToWire(stored with { Grouping = grouping }, grouping, produceClusters);
    }

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

    /// <summary>
    /// Whether a run should ask for the second grouping: what the caller said, else what is
    /// remembered, else yes. Absent means "remembered" rather than "no", so a renderer that does not
    /// know about the field cannot silently make analyses cheaper and less useful.
    /// </summary>
    private async Task<bool> ProduceClustersAsync(bool? requested, CancellationToken cancellationToken)
    {
        if (requested is { } asked)
        {
            return asked;
        }

        var setting = await settings.GetAsync(ProduceClustersKey, cancellationToken).ConfigureAwait(false);

        return !string.Equals(setting, "false", StringComparison.Ordinal);
    }

    [JsonRpcMethod("analysis.setReviewed")]
    public async Task<ReviewedState> SetReviewedAsync(
        SetNodesReviewedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await store
            .GetLatestAsync(request.RepositoryPath, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            throw RpcErrors.Failure(
                "analysis_not_found",
                $"No analysis is stored for '{request.RepositoryPath}'.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = request.RepositoryPath });
        }

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
}
