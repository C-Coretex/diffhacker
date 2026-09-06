using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Llm;

/// <summary>
/// The tool-calling loop.
/// <para>
/// Microsoft.Extensions.AI ships one of these — <c>UseFunctionInvocation()</c> — and this does
/// not use it. Four things that belong to this layer are not expressible from outside that
/// loop: the budget hard stops, the ordered tool-call trace Iteration 13 renders, the per-turn
/// events a live run view needs, and a retry policy that can tell a rate limit from a revoked
/// key. Owning the loop is a hundred lines; bolting all four onto someone else's is more.
/// </para>
/// <para>
/// Single-use and not thread-safe: one session, one run. Usage accumulates on the instance so
/// that a cancelled run — which throws rather than returning — can still be asked what it
/// spent.
/// </para>
/// </summary>
internal sealed partial class LlmSession : ILlmSession
{
    private readonly IChatClient _chat;
    private readonly HttpClient _httpClient;
    private readonly LlmProviderProfile _profile;
    private readonly LlmBudget _budget;
    private readonly ITokenPricing _pricing;
    private readonly ILogger<LlmSession> _logger;
    private readonly Func<double> _jitter;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly List<LlmUsage> _requestUsages = [];
    private readonly List<LlmToolCallRecord> _toolCalls = [];

    private LlmUsage _cumulative;
    private bool _hasRun;

    public LlmSession(
        IChatClient chat,
        HttpClient httpClient,
        LlmProviderProfile profile,
        LlmBudget budget,
        ITokenPricing pricing,
        ILogger<LlmSession> logger,
        Func<double>? jitter = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _chat = chat;
        _httpClient = httpClient;
        _profile = profile;
        _budget = budget;
        _pricing = pricing;
        _logger = logger;

        // Injected so retry tests assert the curve rather than sleeping through it.
        _jitter = jitter ?? RetryPolicy.Jitter;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    public LlmUsage CumulativeUsage => _cumulative;

    public IReadOnlyList<LlmUsage> RequestUsages => _requestUsages;

    public IReadOnlyList<LlmToolCallRecord> ToolCalls => _toolCalls;

    public async Task<LlmRunResult> RunAsync(
        LlmConversation conversation,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (_hasRun)
        {
            throw new InvalidOperationException("An LLM session runs once. Create another.");
        }

        _hasRun = true;

        var tools = conversation.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var mode = conversation.ResponseFormat is null
            ? StructuredOutputMode.PromptOnly
            : StructuredOutput.PreferredMode(_profile.ProviderType);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt(conversation, mode)),
            new(ChatRole.User, conversation.UserMessage),
        };

        var turn = 0;
        var consecutiveToolFailures = 0;
        var repairsUsed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++turn > _budget.MaxTurns)
            {
                return BudgetStop($"The run reached its limit of {_budget.MaxTurns} turns.", turn - 1);
            }

            if (BudgetExceeded() is { } exceeded)
            {
                return BudgetStop(exceeded, turn - 1);
            }

            progress?.Report(new LlmRunEvent
            {
                Kind = LlmRunEventKind.TurnStarted,
                Turn = turn,
                CumulativeUsage = _cumulative,
            });

            var attempt = await SendAsync(messages, conversation, tools.Values, mode, turn, progress, cancellationToken)
                .ConfigureAwait(false);

            if (attempt.Downgraded is { } weaker)
            {
                // The provider refused the response format rather than the request. Try again
                // in a weaker mode instead of failing a run over how the question was phrased.
                FormatDowngraded(_logger, _profile.Id, mode.ToString(), weaker.ToString());
                mode = weaker;
                messages[0] = new ChatMessage(ChatRole.System, SystemPrompt(conversation, mode));
                turn--;
                continue;
            }

            if (attempt.Failure is { } failure)
            {
                progress?.Report(new LlmRunEvent
                {
                    Kind = LlmRunEventKind.TurnFinished,
                    Turn = turn,
                    IsError = true,
                    ReasonCode = failure.FailureCode,
                    CumulativeUsage = _cumulative,
                });

                return Failed(failure, turn);
            }

            var response = attempt.Response!;
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToArray();

            var submission = calls.FirstOrDefault(call =>
                string.Equals(call.Name, StructuredOutput.SubmitToolName, StringComparison.Ordinal));

            var realCalls = calls.Where(call => call != submission).ToArray();

            // A submitted answer ends the run even if the model also asked for tools in the
            // same turn: it has said it is finished, and dispatching the rest would produce
            // results nothing will ever read.
            if (submission is not null || realCalls.Length == 0)
            {
                var answer = submission is not null
                    ? JsonSerializer.Serialize(submission.Arguments)
                    : StructuredOutput.ExtractJson(response.Text);

                progress?.Report(new LlmRunEvent
                {
                    Kind = LlmRunEventKind.TurnFinished,
                    Turn = turn,
                    CumulativeUsage = _cumulative,
                });

                if (ProviderErrorMapper.ClassifyFinishReason(response.FinishReason?.Value) is { } filtered)
                {
                    return Failed(
                        new LlmFailure(filtered, response.Text, null, false, null),
                        turn);
                }

                if (conversation.ResponseFormat is null)
                {
                    return Completed(response.Text, structuredJson: null, turn);
                }

                var errors = StructuredOutput.Validate(answer, conversation.ResponseFormat);
                if (errors.Count == 0)
                {
                    return Completed(response.Text, answer, turn);
                }

                if (repairsUsed++ == 0)
                {
                    // One repair round trip. The model is told exactly what was wrong, which
                    // works far more often than asking again in the same words.
                    ResponseInvalid(_logger, _profile.Id, errors.Count);
                    messages.Add(new ChatMessage(ChatRole.User, RepairPrompt(errors)));
                    continue;
                }

                return Failed(
                    new LlmFailure(
                        LlmFailures.InvalidResponse,
                        "The response did not match the required schema: " + string.Join("; ", errors.Take(5)),
                        null,
                        false,
                        null),
                    turn);
            }

            var results = await DispatchAsync(realCalls, tools, turn, progress, cancellationToken)
                .ConfigureAwait(false);

            consecutiveToolFailures = results.All(result => result.IsError)
                ? consecutiveToolFailures + 1
                : 0;

            messages.Add(new ChatMessage(ChatRole.Tool, [.. results.Select(result => result.Content)]));

            progress?.Report(new LlmRunEvent
            {
                Kind = LlmRunEventKind.TurnFinished,
                Turn = turn,
                CumulativeUsage = _cumulative,
            });

            if (consecutiveToolFailures >= _budget.MaxConsecutiveToolFailures)
            {
                return BudgetStop(
                    $"Every tool call failed {consecutiveToolFailures} turns in a row.",
                    turn);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _chat.Dispose();

        // The session owns its transport: LlmSessionFactory hands each one its own HttpClient
        // so that finishing a run cannot close connections the next one is using.
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>One send, including its retries and one possible format downgrade.</summary>
    private async Task<SendAttempt> SendAsync(
        List<ChatMessage> messages,
        LlmConversation conversation,
        IEnumerable<LlmToolDefinition> tools,
        StructuredOutputMode mode,
        int turn,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        var options = BuildOptions(conversation, tools, mode);
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_budget.RequestTimeout);

            try
            {
                var response = await _chat.GetResponseAsync(messages, options, timeout.Token).ConfigureAwait(false);
                RecordUsage(response.Usage);

                progress?.Report(new LlmRunEvent
                {
                    Kind = LlmRunEventKind.UsageUpdated,
                    Turn = turn,
                    CumulativeUsage = _cumulative,
                });

                return new SendAttempt { Response = response };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up. Unwind rather than classifying this as a failure —
                // CumulativeUsage stays readable, which is how a cancelled run still reports
                // what it spent.
                throw;
            }
#pragma warning disable CA1031 // Every provider failure is classified; none may escape as an opaque crash.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var failure = ProviderErrorMapper.Classify(ex);

                if (conversation.ResponseFormat is not null
                    && StructuredOutput.IsUnsupportedFormat(failure)
                    && StructuredOutput.Downgrade(mode) is { } weaker)
                {
                    return new SendAttempt { Downgraded = weaker };
                }

                attempt++;
                var delay = RetryPolicy.NextDelay(failure, attempt, _budget.MaxRetryAttempts, _jitter());

                if (delay is null)
                {
                    RequestFailed(_logger, _profile.Id, failure.FailureCode, failure.HttpStatus ?? 0);
                    return new SendAttempt { Failure = failure };
                }

                RequestRetrying(_logger, _profile.Id, failure.FailureCode, attempt, delay.Value.TotalSeconds);

                progress?.Report(new LlmRunEvent
                {
                    Kind = LlmRunEventKind.RetryScheduled,
                    Turn = turn,
                    RetryAttempt = attempt,
                    RetryDelay = delay,
                    ReasonCode = failure.FailureCode,
                    CumulativeUsage = _cumulative,
                });

                await _delay(delay.Value, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs every tool the model asked for. Concurrently when it asked for several, because a
    /// model that requests six file reads at once should not wait six round trips for them.
    /// </summary>
    private async Task<ToolOutcome[]> DispatchAsync(
        FunctionCallContent[] calls,
        Dictionary<string, LlmToolDefinition> tools,
        int turn,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        var ordinalBase = _toolCalls.Count;

        var outcomes = await Task.WhenAll(calls.Select((call, index) =>
                InvokeAsync(call, index, tools, turn, progress, cancellationToken)))
            .ConfigureAwait(false);

        // Recorded after the fact and in request order: the calls ran concurrently, but the
        // trace has to read the way the model asked for them.
        foreach (var (outcome, index) in outcomes.Select((outcome, index) => (outcome, index)))
        {
            _toolCalls.Add(outcome.Record with { Ordinal = ordinalBase + index + 1 });
        }

        return outcomes;
    }

    private async Task<ToolOutcome> InvokeAsync(
        FunctionCallContent call,
        int index,
        Dictionary<string, LlmToolDefinition> tools,
        int turn,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        var argumentsJson = JsonSerializer.Serialize(call.Arguments);
        var preview = LlmToolCallRecord.Preview(argumentsJson);

        progress?.Report(new LlmRunEvent
        {
            Kind = LlmRunEventKind.ToolCallStarted,
            Turn = turn,
            ToolName = call.Name,
            ArgumentsPreview = preview,
            CumulativeUsage = _cumulative,
        });

        var stopwatch = Stopwatch.StartNew();
        LlmToolResult result;

        if (!tools.TryGetValue(call.Name, out var tool))
        {
            // Handed back rather than thrown. A model that invented a tool name corrects itself
            // when told; ending the run would waste everything it had already learned.
            result = LlmToolResult.Failure(
                $"There is no tool named '{call.Name}'. Available tools: {string.Join(", ", tools.Keys)}.");
        }
        else
        {
            try
            {
                result = await tool.Invoke(argumentsJson, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A broken tool must not take the run down with it.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ToolThrew(_logger, call.Name, ex);
                result = LlmToolResult.Failure($"The tool failed: {ex.Message}");
            }
        }

        stopwatch.Stop();
        var bytes = Encoding.UTF8.GetByteCount(result.Content);

        progress?.Report(new LlmRunEvent
        {
            Kind = LlmRunEventKind.ToolCallFinished,
            Turn = turn,
            ToolName = call.Name,
            ResultBytes = bytes,
            Duration = stopwatch.Elapsed,
            IsError = result.IsError,
            CumulativeUsage = _cumulative,
        });

        ToolCalled(_logger, call.Name, bytes, stopwatch.ElapsedMilliseconds);

        return new ToolOutcome
        {
            Content = new FunctionResultContent(call.CallId, result.Content),
            IsError = result.IsError,
            Record = new LlmToolCallRecord
            {
                Ordinal = index,
                Turn = turn,
                ToolName = call.Name,
                ArgumentsPreview = preview,
                ResultBytes = bytes,
                Duration = stopwatch.Elapsed,
                IsError = result.IsError,
            },
        };
    }

    private ChatOptions BuildOptions(
        LlmConversation conversation,
        IEnumerable<LlmToolDefinition> tools,
        StructuredOutputMode mode)
    {
        var options = new ChatOptions
        {
            ModelId = _profile.Model,
            Tools = [.. tools.Select(ToolAdapter.ToAiFunction)],
        };

        if (conversation.ResponseFormat is { } format
            && StructuredOutput.Apply(options, format, mode) is { } submitTool)
        {
            options.Tools = [.. options.Tools!, submitTool];
        }

        return options;
    }

    private static string SystemPrompt(LlmConversation conversation, StructuredOutputMode mode) =>
        conversation.ResponseFormat is { } format
            ? conversation.SystemPrompt + StructuredOutput.PromptSuffix(format, mode)
            : conversation.SystemPrompt;

    private static string RepairPrompt(IReadOnlyList<string> errors) =>
        $"""
         Your last answer did not match the required JSON Schema:

         {string.Join(Environment.NewLine, errors.Take(20).Select(error => "- " + error))}

         Reply with the corrected JSON alone. Do not explain, and do not call any more tools.
         """;

    private void RecordUsage(UsageDetails? usage)
    {
        var recorded = new LlmUsage
        {
            InputTokens = usage?.InputTokenCount ?? 0,
            OutputTokens = usage?.OutputTokenCount ?? 0,
            CachedInputTokens = CachedTokensOf(usage),
            IsReported = usage is not null,
        };

        var priced = recorded with { EstimatedCostUsd = CostOf(recorded) };

        _requestUsages.Add(priced);
        _cumulative += priced;
    }

    /// <summary>
    /// Cached-input tokens, where the provider reports them. MEAI surfaces the ones it does not
    /// model itself in <see cref="UsageDetails.AdditionalCounts"/>, and the providers spell the
    /// key differently.
    /// </summary>
    private static long CachedTokensOf(UsageDetails? usage)
    {
        if (usage?.AdditionalCounts is not { } counts)
        {
            return 0;
        }

        foreach (var key in (string[])["InputTokenDetails.CachedTokenCount", "cache_read_input_tokens", "cached_tokens"])
        {
            if (counts.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return 0;
    }

    private decimal? CostOf(LlmUsage usage)
    {
        // The profile's own rate wins: it is the user telling us the table is out of date for
        // their model, which it eventually will be for every model.
        var rate = _profile.CostOverride
                   ?? (_pricing.TryGetRate(_profile.ProviderType, _profile.Model, out var known) ? known : null);

        return rate?.CostOf(usage);
    }

    private string? BudgetExceeded()
    {
        if (_toolCalls.Count >= _budget.MaxToolCalls)
        {
            return $"The run reached its limit of {_budget.MaxToolCalls} tool calls.";
        }

        if (_cumulative.TotalTokens >= _budget.MaxTotalTokens)
        {
            return $"The run reached its limit of {_budget.MaxTotalTokens:N0} tokens.";
        }

        if (_budget.MaxCostUsd is { } ceiling && _cumulative.EstimatedCostUsd >= ceiling)
        {
            return $"The run reached its cost ceiling of ${ceiling:N2}.";
        }

        return null;
    }

    private LlmRunResult Completed(string? text, string? structuredJson, int turn) => new()
    {
        Outcome = LlmRunOutcome.Completed,
        Text = text,
        StructuredJson = structuredJson,
        Usage = _cumulative,
        TurnCount = turn,
        ToolCalls = _toolCalls,
    };

    private LlmRunResult Failed(LlmFailure failure, int turn) => new()
    {
        // A context overflow is its own outcome, not a failure code with a label. Iteration 7
        // has to branch on it (requirement 6), and that reads better as an outcome than as a
        // string comparison at the call site.
        Outcome = failure.FailureCode == LlmFailures.ContextOverflow
            ? LlmRunOutcome.ContextOverflow
            : LlmRunOutcome.Failed,
        FailureCode = failure.FailureCode,
        ProviderMessage = failure.ProviderMessage,
        Usage = _cumulative,
        TurnCount = turn,
        ToolCalls = _toolCalls,
    };

    private LlmRunResult BudgetStop(string explanation, int turn)
    {
        BudgetStopped(_logger, _profile.Id, explanation);

        return new LlmRunResult
        {
            Outcome = LlmRunOutcome.BudgetExceeded,
            FailureCode = LlmFailures.BudgetExceeded,

            // Deliberately spelled out. §0.2.8 and Iteration 13 requirement 7 both insist that
            // a partial result is never presented as a complete one, which needs the reader to
            // know which limit stopped it and how far it got.
            ProviderMessage =
                $"{explanation} {_toolCalls.Count} tool call(s) and {_cumulative.TotalTokens:N0} token(s) "
                + "were used, and no final answer was produced.",
            Usage = _cumulative,
            TurnCount = turn,
            ToolCalls = _toolCalls,
        };
    }

    private sealed record SendAttempt
    {
        public ChatResponse? Response { get; init; }

        public LlmFailure? Failure { get; init; }

        public StructuredOutputMode? Downgraded { get; init; }
    }

    private sealed record ToolOutcome
    {
        public required FunctionResultContent Content { get; init; }

        public required bool IsError { get; init; }

        public required LlmToolCallRecord Record { get; init; }
    }

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Tool {ToolName} returned {ResultBytes} byte(s) in {ElapsedMs} ms.")]
    private static partial void ToolCalled(ILogger logger, string toolName, int resultBytes, long elapsedMs);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Warning,
        Message = "Tool {ToolName} threw.")]
    private static partial void ToolThrew(ILogger logger, string toolName, Exception exception);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Warning,
        Message = "Request to provider {ProfileId} failed as {FailureCode} (HTTP {Status}).")]
    private static partial void RequestFailed(ILogger logger, string profileId, string failureCode, int status);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Information,
        Message = "Provider {ProfileId} returned {FailureCode}; retry {Attempt} in {DelaySeconds:0.0}s.")]
    private static partial void RequestRetrying(
        ILogger logger, string profileId, string failureCode, int attempt, double delaySeconds);

    [LoggerMessage(
        EventId = 4006,
        Level = LogLevel.Information,
        Message = "Provider {ProfileId} rejected structured output mode {From}; retrying as {To}.")]
    private static partial void FormatDowngraded(ILogger logger, string profileId, string from, string to);

    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Warning,
        Message = "Provider {ProfileId} returned a response with {ErrorCount} schema violation(s); repairing.")]
    private static partial void ResponseInvalid(ILogger logger, string profileId, int errorCount);

    [LoggerMessage(
        EventId = 4008,
        Level = LogLevel.Warning,
        Message = "Run on provider {ProfileId} hit a budget limit: {Explanation}")]
    private static partial void BudgetStopped(ILogger logger, string profileId, string explanation);
}
