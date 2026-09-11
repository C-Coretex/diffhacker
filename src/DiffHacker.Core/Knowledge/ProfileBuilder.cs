using System.Diagnostics;
using System.Text.Json;
using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Settings;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Core.Knowledge;

/// <summary>
/// Profiles a repository: one LLM run, exploring through the toolbox, answering with the document
/// shape in <c>schema/project-profile-document.schema.json</c>.
/// <para>
/// It joins pieces that until now had no consumer — <c>ILlmSessionFactory</c>, the toolbox, the
/// progress sink, the price table — so this class is where a great deal of earlier work is first
/// proven to fit together.
/// </para>
/// <para>
/// Two things are deliberate about how it ends. A run that is over the size budget is handed back
/// once to be shortened and, if that does not bring it under, is <b>saved anyway</b> rather than
/// being truncated or failed: a profile cut off mid-sentence would misinform every future analysis
/// quietly and forever, and the character budget is the user's own dial (§0.6), not a runaway
/// guard — discarding a complete, valid profile because it is merely long serves nobody. The
/// reviewer sees it is oversized from <see cref="ProfileProvenance.DocumentCharacters"/> against
/// <see cref="ProjectProfile.EffectiveCharacterBudget"/> and can raise the budget or trim it by
/// hand. And a cancelled run records what it spent before rethrowing, because the money is gone
/// whether or not the profile was produced and the user is entitled to know how much.
/// </para>
/// </summary>
public sealed partial class ProfileBuilder(
    IProviderProfileStore providers,
    ILlmSessionFactory sessions,
    IRepositoryToolboxFactory toolboxes,
    IProjectProfileStore profiles,
    IGitClient git,
    IToolProgressSink progressSink,
    IBudgetDecisionPrompt budgetPrompt,
    TimeProvider clock,
    ILogger<ProfileBuilder> logger) : IProfileBuilder
{
    /// <summary>
    /// Web defaults map the schema's camelCase onto the domain record's properties one for one, so
    /// the model's answer becomes a <see cref="ProjectProfileDocument"/> with no mapping layer to
    /// drift out of step with the schema both sides were generated from.
    /// </summary>
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);

    /// <summary>Identifier sent to providers that name their schemas. Underscores only.</summary>
    private const string SchemaName = "project_profile";

    private const string SchemaKey = "project-profile-document";

    public async Task<ProfileBuildResult> BuildAsync(
        string repositoryPath,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var startedAt = clock.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();

        var provider = await ActiveProvider.ResolveAsync(providers, cancellationToken).ConfigureAwait(false);

        if (provider is null)
        {
            return NoProviderResult(repositoryPath, startedAt);
        }

        var stored = await profiles.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false)
            ?? ProjectProfile.Empty(repositoryPath, startedAt);

        var budget = stored.EffectiveCharacterBudget;
        var recorder = new RecordingProgressSink(progressSink);

        var toolbox = await toolboxes
            .OpenAsync(repositoryPath, stored.EffectiveExcludedGlobs, recorder, cancellationToken)
            .ConfigureAwait(false);

        var visibleFiles = await git
            .ListFilesAsync(new FileListQuery(repositoryPath), cancellationToken)
            .ConfigureAwait(false);

        var conversation = new LlmConversation
        {
            SystemPrompt = ProfilePrompt.SystemPrompt(budget),
            UserMessage = ProfilePrompt.OpeningMessage(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(repositoryPath)),
                visibleFiles.Count,
                ProfilePrompt.FindDocumentation(visibleFiles),
                stored.CustomInstructions),
            Tools = toolbox.Tools,
            ResponseFormat = new LlmResponseFormat
            {
                SchemaName = SchemaName,
                SchemaJson = ContractSchemas.Get(SchemaKey),
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

            // Recorded before the throw. The tokens were spent whether or not a profile came out
            // of them, and a cancelled run that reports nothing looks free.
            var cancelled = Record(
                repositoryPath, provider, startedAt, stopwatch.Elapsed, ProfileRunOutcome.Cancelled,
                null, session.CumulativeUsage, session.ToolCalls, recorder.Messages);

            await profiles.RecordRunAsync(cancelled, CancellationToken.None).ConfigureAwait(false);
            RunCancelled(logger, repositoryPath, session.ToolCalls.Count);
            throw;
        }

        if (!run.Succeeded)
        {
            return await FailAsync(
                repositoryPath, provider, startedAt, stopwatch, run.FailureCode ?? LlmFailures.UnexpectedResponse,
                run.ProviderMessage, session, recorder, cancellationToken).ConfigureAwait(false);
        }

        var document = Deserialise(run.StructuredJson);

        if (document is null)
        {
            return await FailAsync(
                repositoryPath, provider, startedAt, stopwatch, ProfileFailures.UnreadableAnswer,
                null, session, recorder, cancellationToken).ConfigureAwait(false);
        }

        var size = ProfileBudget.Measure(document);

        if (size > budget)
        {
            OverBudget(logger, size, budget);

            var shortened = await ShortenAsync(document, size, budget, provider, cancellationToken)
                .ConfigureAwait(false);

            if (shortened is not null)
            {
                var shortenedSize = ProfileBudget.Measure(shortened);

                // Only kept if it actually helped. A repair that came back the same length, longer,
                // or unreadable leaves the original — already a complete, valid profile — in place.
                if (shortenedSize < size)
                {
                    document = shortened;
                    size = shortenedSize;
                }
            }

            if (size > budget)
            {
                StillOverBudget(logger, size, budget);
            }
        }

        stopwatch.Stop();

        var commit = await git.GetHeadCommitAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        var provenance = new ProfileProvenance
        {
            CommitSha = commit,
            GeneratedAtUtc = clock.GetUtcNow(),
            ProviderDisplayName = provider.DisplayName,
            Model = provider.Model,
            DocumentCharacters = size,
        };

        await profiles
            .SaveGeneratedAsync(repositoryPath, document, provenance, cancellationToken)
            .ConfigureAwait(false);

        var record = Record(
            repositoryPath, provider, startedAt, stopwatch.Elapsed, ProfileRunOutcome.Completed,
            null, session.CumulativeUsage, session.ToolCalls, recorder.Messages);

        await profiles.RecordRunAsync(record, cancellationToken).ConfigureAwait(false);

        ProfileStored(logger, repositoryPath, size, session.ToolCalls.Count, run.TurnCount);

        var saved = await profiles.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        return new ProfileBuildResult
        {
            Outcome = ProfileRunOutcome.Completed,
            Profile = saved,
            Run = record,
        };
    }

    /// <summary>
    /// Hands an over-long document back to be shortened, once.
    /// <para>
    /// A fresh session rather than another turn of the first one: a session runs once by contract,
    /// and the repair does not need the exploration history — the model is editing its own prose,
    /// not learning anything new. No tools are offered, so this is one request, and the document
    /// it is given is its own output rather than anything read from the repository, which is why
    /// putting it in the prompt does not cross §0.2.9.
    /// </para>
    /// </summary>
    private async Task<ProjectProfileDocument?> ShortenAsync(
        ProjectProfileDocument document,
        int actual,
        int budget,
        Providers.LlmProviderProfile provider,
        CancellationToken cancellationToken)
    {
        await using var session = await sessions
            .CreateAsync(provider, LlmBudget.Default with { MaxTurns = 2, MaxToolCalls = 0 }, cancellationToken)
            .ConfigureAwait(false);

        var conversation = new LlmConversation
        {
            SystemPrompt = "You shorten a project profile you already wrote. Same document, same "
                + "structure, fewer characters. Never invent, never drop a section.",
            UserMessage = ProfilePrompt.OverBudgetRepair(actual, budget)
                + "\n\nThe document was:\n\n"
                + JsonSerializer.Serialize(document, DocumentJson),
            ResponseFormat = new LlmResponseFormat
            {
                SchemaName = SchemaName,
                SchemaJson = ContractSchemas.Get(SchemaKey),
            },
        };

        var run = await session.RunAsync(conversation, null, cancellationToken).ConfigureAwait(false);

        return run.Succeeded ? Deserialise(run.StructuredJson) : null;
    }

    private static ProjectProfileDocument? Deserialise(string? structuredJson)
    {
        if (string.IsNullOrWhiteSpace(structuredJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectProfileDocument>(structuredJson, DocumentJson);
        }
        catch (JsonException)
        {
            // The answer validated against the schema before it got here, so this is close to
            // unreachable — but "close to" is not "is", and a failed profile is better than a
            // crashed one.
            return null;
        }
    }

    private async Task<ProfileBuildResult> FailAsync(
        string repositoryPath,
        Providers.LlmProviderProfile provider,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string failureCode,
        string? providerMessage,
        ILlmSession session,
        RecordingProgressSink recorder,
        CancellationToken cancellationToken)
    {
        stopwatch.Stop();

        var record = Record(
            repositoryPath, provider, startedAt, stopwatch.Elapsed, ProfileRunOutcome.Failed,
            failureCode, session.CumulativeUsage, session.ToolCalls, recorder.Messages);

        await profiles.RecordRunAsync(record, cancellationToken).ConfigureAwait(false);
        RunFailed(logger, repositoryPath, failureCode);

        return new ProfileBuildResult
        {
            Outcome = ProfileRunOutcome.Failed,
            Run = record,
            FailureCode = failureCode,
            ProviderMessage = providerMessage,
        };
    }

    private static ProfileBuildResult NoProviderResult(string repositoryPath, DateTimeOffset startedAt) =>
        new()
        {
            Outcome = ProfileRunOutcome.Failed,
            FailureCode = ProfileFailures.NoProvider,
            Run = new ProfileRun
            {
                Id = Guid.NewGuid().ToString("N"),
                RepositoryPath = repositoryPath,
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt,
                Outcome = ProfileRunOutcome.Failed,
                FailureCode = ProfileFailures.NoProvider,
                ProviderDisplayName = string.Empty,
                Model = string.Empty,
            },
        };

    private ProfileRun Record(
        string repositoryPath,
        Providers.LlmProviderProfile provider,
        DateTimeOffset startedAt,
        TimeSpan duration,
        ProfileRunOutcome outcome,
        string? failureCode,
        LlmUsage usage,
        IReadOnlyList<LlmToolCallRecord> toolCalls,
        IReadOnlyList<string> progressMessages) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            RepositoryPath = repositoryPath,
            StartedAtUtc = startedAt,
            FinishedAtUtc = clock.GetUtcNow(),
            Outcome = outcome,
            FailureCode = failureCode,
            ProviderDisplayName = provider.DisplayName,
            Model = provider.Model,
            Usage = usage,
            Duration = duration,
            ToolCalls = toolCalls,
            ProgressMessages = progressMessages,
        };

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "Stored a project profile for {Repository}: {Characters} characters, {ToolCalls} tool calls over {Turns} turns.")]
    private static partial void ProfileStored(ILogger logger, string repository, int characters, int toolCalls, int turns);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Warning,
        Message = "The profile came back at {Actual} characters against a budget of {Budget}; asking for a shorter one.")]
    private static partial void OverBudget(ILogger logger, int actual, int budget);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Warning,
        Message = "Profiling {Repository} failed: {FailureCode}.")]
    private static partial void RunFailed(ILogger logger, string repository, string failureCode);

    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Information,
        Message = "Profiling {Repository} was cancelled after {ToolCalls} tool calls.")]
    private static partial void RunCancelled(ILogger logger, string repository, int toolCalls);

    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Warning,
        Message = "The profile is still {Actual} characters against a budget of {Budget} after one repair; "
            + "saving it anyway rather than truncating or failing.")]
    private static partial void StillOverBudget(ILogger logger, int actual, int budget);
}
