using System.Diagnostics;
using System.Text.Json;
using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Settings;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// One analysis, end to end: the changeset, one LLM conversation through the toolbox, validation,
/// and a persisted result.
/// <para>
/// The validation is handed to the session rather than run after it, and that is the shape of the
/// whole class. A result checked afterwards can only be fixed by a model that has forgotten
/// everything it read; a result checked inside the loop is fixed by one that still has the
/// repository open in front of it. Requirement 4's "feed the specific failure back" is then
/// literal — the diagnostics naming the offending path go into the same conversation.
/// </para>
/// <para>
/// Nothing partial is ever stored. A cancelled run rethrows with its usage readable on the session,
/// a failed one comes back as a result carrying what it spent, and only a run whose answer passed
/// every rule reaches <see cref="IAnalysisStore"/>. §0.2.8 is a promise about what the user sees,
/// and it is kept here rather than in the renderer.
/// </para>
/// </summary>
public sealed partial class AnalysisRunner(
    IProviderProfileStore providers,
    ILlmSessionFactory sessions,
    IRepositoryToolboxFactory toolboxes,
    IProjectProfileStore profiles,
    IAnalysisStore analyses,
    IGitClient git,
    IToolProgressSink progressSink,
    IBudgetDecisionPrompt budgetPrompt,
    TimeProvider clock,
    ILogger<AnalysisRunner> logger) : IAnalysisRunner
{
    /// <summary>
    /// The floor and ceiling for how many times a rejected result may be handed back, scaled by
    /// how many files changed.
    /// <para>
    /// A fixed count either wastes rounds on a small change — one round fixes what was reported,
    /// a second catches what the fix knocked over (moving a node between containers can leave the
    /// one it left without an entry node), a third is rarely the round that converges — or runs
    /// out on a thousand-file one before a first pass's small, independent mistakes across many
    /// nodes are all fixed. Every round re-emits the whole document at the model's output rate, so
    /// growth is capped rather than unbounded: failing loudly beats an unbounded argument.
    /// </para>
    /// <para>
    /// Two ceilings, not one. A schema violation (wrong type, missing field) is structural and
    /// usually a one-shot fix regardless of size, so its floor and ceiling both stay lower than
    /// the domain-rule repairs (§0.2.5's "every file covered", dangling edges, entry nodes) that
    /// scale with how much there is to get right.
    /// </para>
    /// </summary>
    private const int SchemaRepairFloor = 1;

    private const int SchemaRepairCeiling = 5;

    private const int ResultRepairFloor = 2;

    private const int ResultRepairCeiling = 6;

    /// <summary>One extra round for every this many files past the floor.</summary>
    private const int FilesPerExtraRound = 100;

    private static int RepairRoundsFor(int fileCount, int floor, int ceiling) =>
        Math.Clamp(floor + (fileCount / FilesPerExtraRound), floor, ceiling);

    /// <inheritdoc cref="ProfileBuilder"/>
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);

    public async Task<AnalysisRunResult> RunAsync(
        string repositoryPath,
        AnalysisRunOptions options,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var provider = await ActiveProvider.ResolveAsync(providers, cancellationToken).ConfigureAwait(false);

        if (provider is null)
        {
            return new AnalysisRunResult
            {
                FailureCode = AnalysisFailures.NoProvider,
                Duration = stopwatch.Elapsed,
            };
        }

        // Hashed here, at the start, from the same changeset the answer is validated against: the
        // fingerprint describes the change the model was shown, which is what a later freshness
        // check has to compare with.
        var changeset = await git
            .GetChangesetAsync(new ChangesetQuery(repositoryPath, HashContent: true), cancellationToken)
            .ConfigureAwait(false);

        if (changeset.IsClean)
        {
            // Checked before the toolbox is opened and long before a token is spent: there is
            // nothing to describe, and asking a model to describe it anyway would cost money to
            // be told so.
            return new AnalysisRunResult
            {
                FailureCode = AnalysisFailures.CleanChangeset,
                Duration = stopwatch.Elapsed,
            };
        }

        var storedProfile = await profiles.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        var recorder = new RecordingProgressSink(progressSink);

        var toolbox = await toolboxes
            .OpenAsync(
                repositoryPath,
                storedProfile?.EffectiveExcludedGlobs ?? SensitiveFiles.Effective([]),
                recorder,
                cancellationToken)
            .ConfigureAwait(false);

        // Captured from the last validation the session ran, so the accepted answer is not parsed
        // and checked a second time, and a rejected one still has its diagnostics to report.
        AnalysisResult? candidate = null;
        AnalysisValidation? validation = null;

        var conversation = new LlmConversation
        {
            SystemPrompt = AnalysisPrompt.SystemPrompt(options),
            UserMessage = AnalysisPrompt.OpeningMessage(RepositoryName(repositoryPath), changeset, storedProfile),
            Tools = toolbox.Tools,
            ResponseFormat = new LlmResponseFormat
            {
                SchemaName = AnalysisPrompt.SchemaName,

                // The prompt and the schema agree about which parts exist, or the model is asked for
                // a field it was told nothing about and told about a field it cannot answer in.
                SchemaJson = AnalysisResponseSchema.For(options),
            },
            MaxSchemaRepairs = RepairRoundsFor(changeset.Files.Count, SchemaRepairFloor, SchemaRepairCeiling),
            MaxResultRepairs = RepairRoundsFor(changeset.Files.Count, ResultRepairFloor, ResultRepairCeiling),
            ResultValidator = json =>
            {
                candidate = Deserialise(json);

                if (candidate is null)
                {
                    validation = null;
                    return ["The answer could not be read back as an analysis document."];
                }

                validation = AnalysisValidator.Validate(candidate, changeset.Files, options);
                return validation.ErrorMessages;
            },
        };

        await using var session = await sessions
            .CreateAsync(provider, provider.EffectiveBudget, cancellationToken)
            .ConfigureAwait(false);

        LlmRunResult run;

        try
        {
            run = await session
                .RunAsync(conversation, progress, cancellationToken, budgetPrompt.AskAsync)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();

            // Nothing stored, but the spend is reported: the tokens are gone whether or not a
            // graph came out of them, and a cancelled run that reports nothing looks free.
            RunCancelled(logger, repositoryPath, session.ToolCalls.Count, session.CumulativeUsage.TotalTokens);
            throw;
        }

        stopwatch.Stop();

        if (!run.Succeeded)
        {
            return Failure(
                repositoryPath,
                run.FailureCode ?? LlmFailures.UnexpectedResponse,
                run.ProviderMessage,
                validation?.Errors ?? [],
                session,
                stopwatch.Elapsed);
        }

        if (candidate is null || validation is null || !validation.IsValid)
        {
            // Unreachable by contract — the session only completes on an answer the validator
            // accepted — but a wrong result is worse than a failed one, so it is checked anyway.
            return Failure(
                repositoryPath,
                AnalysisFailures.UnreadableAnswer,
                null,
                validation?.Errors ?? [],
                session,
                stopwatch.Elapsed);
        }

        var commit = await git.GetHeadCommitAsync(repositoryPath, cancellationToken).ConfigureAwait(false);
        var graph = AnalysisGraph.Build(candidate.Nodes, candidate.Edges);

        var analysis = new Analysis
        {
            Id = Guid.NewGuid().ToString("N"),
            RepositoryPath = repositoryPath,
            SchemaVersion = ContractVersion.Current,
            CreatedAtUtc = clock.GetUtcNow(),
            HeadCommit = commit,
            ProviderDisplayName = provider.DisplayName,
            Model = provider.Model,
            Usage = session.CumulativeUsage,
            Duration = stopwatch.Elapsed,
            RepairRounds = run.ResultRepairs,
            Document = candidate,

            // Kept beside the answer, so an empty risk list can later be told apart from a run that
            // was never asked for risks.
            Requested = options,

            // Recorded for dependency flow, the grouping an analysis opens in; the four numbers that
            // depend on the grouping are recomputed for the other one on read.
            Statistics = AnalysisStatistics.From(
                candidate,
                AnalysisGrouping.DependencyFlow,
                changeset.Statistics,
                graph),
            // The changeset this answer was validated against, kept with it. Taken from the same
            // object the run used, so the boxes can never disagree with the graph drawn on them.
            ChangedFiles = [.. changeset.Files.Select(ChangedFileFacts.From)],
            Diagnostics = validation.Warnings,
            ToolCalls = session.ToolCalls,
            ProgressMessages = recorder.Messages,
        };

        await analyses.SaveAsync(analysis, cancellationToken).ConfigureAwait(false);

        AnalysisStored(
            logger,
            repositoryPath,
            changeset.Files.Count,
            candidate.Nodes.Count,
            candidate.DependencyContainers.Count,
            candidate.ClusterContainers.Count,
            candidate.ImplementationGroups?.Count ?? 0,
            run.ResultRepairs,
            session.ToolCalls.Count);

        return new AnalysisRunResult
        {
            Analysis = analysis,
            Usage = session.CumulativeUsage,
            Duration = stopwatch.Elapsed,
        };
    }

    private AnalysisRunResult Failure(
        string repositoryPath,
        string failureCode,
        string? providerMessage,
        IReadOnlyList<AnalysisDiagnostic> diagnostics,
        ILlmSession session,
        TimeSpan duration)
    {
        RunFailed(logger, repositoryPath, failureCode, diagnostics.Count);

        return new AnalysisRunResult
        {
            FailureCode = failureCode,
            ProviderMessage = providerMessage,
            Diagnostics = diagnostics,
            Usage = session.CumulativeUsage,
            Duration = duration,
        };
    }

    private static AnalysisResult? Deserialise(string? structuredJson)
    {
        if (string.IsNullOrWhiteSpace(structuredJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AnalysisResult>(structuredJson, DocumentJson);
        }
        catch (JsonException)
        {
            // The answer matched the schema before it got here, so this is close to unreachable —
            // but "close to" is not "is", and a rejected answer is better than a crashed run.
            return null;
        }
    }

    private static string RepositoryName(string repositoryPath) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(repositoryPath));

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Information,
        Message = "Stored an analysis of {Repository}: {Files} changed file(s) as {Nodes} node(s) in "
            + "{Containers} dependency-flow container(s) and {ClusterContainers} change-cluster(s), "
            + "{ImplementationGroups} implementation group(s), {Repairs} repair round(s), "
            + "{ToolCalls} tool calls.")]
    private static partial void AnalysisStored(
        ILogger logger,
        string repository,
        int files,
        int nodes,
        int containers,
        int clusterContainers,
        int implementationGroups,
        int repairs,
        int toolCalls);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Warning,
        Message = "Analysing {Repository} failed: {FailureCode} ({DiagnosticCount} diagnostic(s)).")]
    private static partial void RunFailed(
        ILogger logger,
        string repository,
        string failureCode,
        int diagnosticCount);

    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Information,
        Message = "Analysing {Repository} was cancelled after {ToolCalls} tool calls and {Tokens} tokens.")]
    private static partial void RunCancelled(ILogger logger, string repository, int toolCalls, long tokens);
}
