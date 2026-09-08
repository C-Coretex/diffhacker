using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Settings;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// The analysis surface: run one, or read the stored one back.
/// <para>
/// Both methods return the whole <see cref="AnalysisView"/> rather than a delta, so the renderer
/// replaces its state instead of merging — the same arrangement <see cref="ProfileRpcTarget"/>
/// uses, and for the same reason: an analysis is one artifact and half of a new one on top of half
/// of an old one is not any analysis at all.
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
    RunEventNotifier runEvents,
    ILogger<AnalysisRpcTarget> logger)
{
    [JsonRpcMethod("analysis.get")]
    public async Task<AnalysisView> GetAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await store
            .GetLatestAsync(request.RepositoryPath, cancellationToken)
            .ConfigureAwait(false);

        // Reading never runs anything. That is the whole promise of persisting the result: the
        // conversation happened once and was paid for once.
        return stored is null ? AnalysisWire.Empty(request.RepositoryPath) : AnalysisWire.ToWire(stored);
    }

    [JsonRpcMethod("analysis.run")]
    public async Task<AnalysisView> RunAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AnalysisRunResult result;

        try
        {
            result = await runner
                .RunAsync(request.RepositoryPath, runEvents, cancellationToken)
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

        return AnalysisWire.ToWire(result.Analysis);
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
