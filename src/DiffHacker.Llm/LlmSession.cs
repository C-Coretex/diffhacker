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
    private readonly IModelCatalog _pricing;
    private readonly ILogger<LlmSession> _logger;
    private readonly Func<double> _jitter;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly List<LlmUsage> _requestUsages = [];
    private readonly List<LlmToolCallRecord> _toolCalls = [];

    // Bounds what PruneOldToolResults resends: which message holds which call ids, and which
    // of those have already been shrunk to a stub so a later pass does not redo the work.
    private readonly List<ToolMessageEntry> _toolMessageHistory = [];
    private readonly HashSet<int> _prunedMessageIndices = [];

    // What is filling the context, in characters. Accumulated as messages are added rather than
    // recomputed by walking the transcript, because the transcript is the thing that grows: a walk
    // per turn would be quadratic in the run, and a run is up to three hundred turns.
    private int _instructionCharacters;
    private int _schemaCharacters;
    private int _toolDefinitionCharacters;
    private int _userCharacters;
    private int _assistantCharacters;
    private int _toolResultCharacters;
    private int _prunedCharacters;
    private int _reasoningCharacters;
    private bool _reasoningReported;

    private LlmUsage _cumulative;
    private bool _hasRun;

    // On the session rather than threaded through the loop, for the same reason the usage and
    // the tool calls are: every result that comes out of here has to report it, and a counter
    // passed by hand is one a future branch forgets to pass.
    private int _resultRepairs;

    public LlmSession(
        IChatClient chat,
        HttpClient httpClient,
        LlmProviderProfile profile,
        LlmBudget budget,
        IModelCatalog pricing,
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

        MeasurePreamble(conversation, mode);
        _userCharacters = conversation.UserMessage.Length;

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

                // Carried on the two events a live view actually redraws on: the start of a turn,
                // which is when the transcript has just grown or been pruned, and the arrival of
                // usage, which is when the provider's own count of it becomes known.
                ContextTokens = ContextTokens(),
                ContextWindowTokens = ContextWindow(),
                Context = ContextSnapshot(),
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

                // A downgrade rewrites the system message and changes whether the schema is sent
                // with the request at all, so the fixed part of the measurement is taken again.
                MeasurePreamble(conversation, mode);
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
            MeasureAssistant(response.Messages);

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

                    // Measured again at the end of the turn, not only at its start. The model's
                    // own reply has been added to the transcript since then, and on a turn that
                    // dispatched tools their results have been added and the oldest ones pruned —
                    // so this is the only point at which the whole of a turn's effect is visible.
                    ContextTokens = ContextTokens(),
                    ContextWindowTokens = ContextWindow(),
                    Context = ContextSnapshot(),
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
                    var rejections = CallerRejections(conversation, answer!);

                    if (rejections.Count == 0)
                    {
                        return Completed(response.Text, answer, turn);
                    }

                    if (_resultRepairs < conversation.MaxResultRepairs)
                    {
                        // Back into the same conversation, not a fresh one. The model still has
                        // everything it read in context and its tools are still bound, so a
                        // "no node covers src/Cache.cs" is something it can go and fix properly
                        // rather than paper over from the file list.
                        _resultRepairs++;
                        ResultRejected(_logger, _profile.Id, rejections.Count, _resultRepairs);
                        AcknowledgeSubmission(messages, submission);

                        var resultRepair = ResultRepairPrompt(rejections);
                        messages.Add(new ChatMessage(ChatRole.User, resultRepair));
                        _userCharacters += resultRepair.Length;
                        continue;
                    }

                    return Failed(
                        new LlmFailure(
                            LlmFailures.ResultRejected,
                            "The result did not pass validation after "
                                + $"{_resultRepairs} repair round(s): "
                                + string.Join("; ", rejections.Take(5)),
                            null,
                            false,
                            null),
                        turn,
                        answer);
                }

                if (repairsUsed < conversation.MaxSchemaRepairs)
                {
                    // The model is told exactly what was wrong, which works far more often than
                    // asking again in the same words. MaxSchemaRepairs is usually 1: a structural
                    // mistake is usually a formatting slip fixed from what the model already
                    // wrote, not something more rounds converge on.
                    repairsUsed++;
                    ResponseInvalid(_logger, _profile.Id, errors.Count);
                    AcknowledgeSubmission(messages, submission);

                    var schemaRepair = RepairPrompt(errors);
                    messages.Add(new ChatMessage(ChatRole.User, schemaRepair));
                    _userCharacters += schemaRepair.Length;
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
            _toolResultCharacters += results.Sum(result => ResultTextOf(result.Content).Length);

            _toolMessageHistory.Add(new ToolMessageEntry(
                messages.Count - 1,
                [.. results.Select(result => (result.Content.CallId, result.Record.ToolName))]));

            PruneOldToolResults(messages);

            progress?.Report(new LlmRunEvent
            {
                Kind = LlmRunEventKind.TurnFinished,
                Turn = turn,
                CumulativeUsage = _cumulative,
                ContextTokens = ContextTokens(),
                ContextWindowTokens = ContextWindow(),
                Context = ContextSnapshot(),
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
                // Streamed rather than buffered so the connection keeps receiving bytes while a
                // large answer is generated. A non-streaming call to a large-changeset request
                // sits silent until the whole response is ready, which is exactly the shape of
                // request an idle-duration network timeout kills. This stays internal — the
                // chunks are aggregated back into one ChatResponse before anything downstream
                // sees it, so nothing partial ever reaches the UI (still true to §0.2.8).
                var response = await _chat
                    .GetStreamingResponseAsync(messages, options, timeout.Token)
                    .ToChatResponseAsync(timeout.Token)
                    .ConfigureAwait(false);
                RecordUsage(response.Usage);

                progress?.Report(new LlmRunEvent
                {
                    Kind = LlmRunEventKind.UsageUpdated,
                    Turn = turn,
                    CumulativeUsage = _cumulative,
                    ContextTokens = ContextTokens(),
                    ContextWindowTokens = ContextWindow(),
                    Context = ContextSnapshot(),
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
        var resultPreview = LlmToolCallRecord.PreviewResult(result.Content);

        progress?.Report(new LlmRunEvent
        {
            Kind = LlmRunEventKind.ToolCallFinished,
            Turn = turn,
            ToolName = call.Name,
            ResultBytes = bytes,
            ResultPreview = resultPreview,
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
                ResultPreview = resultPreview,
                Duration = stopwatch.Elapsed,
                IsError = result.IsError,
            },
        };
    }

    /// <summary>
    /// Keeps the transcript's tool-result text within
    /// <see cref="LlmBudget.ToolResultRetentionBytes"/> by replacing the oldest results with a
    /// short stub once the newer ones already cover the budget on their own.
    /// <para>
    /// Every result stays addressable by its original call id — only
    /// <see cref="FunctionResultContent.Result"/> shrinks — so the message list stays a valid
    /// tool-call/tool-result pairing for every provider. Idempotent: a message already pruned
    /// measures small on the next pass, so the walk naturally moves the retention window
    /// forward instead of re-touching it.
    /// </para>
    /// </summary>
    private void PruneOldToolResults(List<ChatMessage> messages)
    {
        //TODO: maybe add reasoning pruning too. If we would add it though, we would need to tell the model explicitly to save important data to the Response Text, not the Reasoning
        var retained = 0L;
        var keepFromIndex = 0;

        for (var i = _toolMessageHistory.Count - 1; i >= 0; i--)
        {
            var entry = _toolMessageHistory[i];
            var content = messages[entry.MessageIndex].Contents.OfType<FunctionResultContent>();
            retained += content.Sum(result => Encoding.UTF8.GetByteCount(result.Result as string ?? string.Empty));

            if (retained > _budget.ToolResultRetentionBytes)
            {
                keepFromIndex = i + 1;
                break;
            }
        }

        for (var i = 0; i < keepFromIndex; i++)
        {
            var entry = _toolMessageHistory[i];
            if (!_prunedMessageIndices.Add(entry.MessageIndex))
            {
                continue;
            }

            var before = messages[entry.MessageIndex].Contents
                .OfType<FunctionResultContent>()
                .Sum(result => ResultTextOf(result).Length);

            messages[entry.MessageIndex] = new ChatMessage(
                ChatRole.Tool,
                [.. entry.Calls.Select(call => (AIContent)new FunctionResultContent(
                    call.CallId,
                    $"[pruned: {call.ToolName} because of the retained window. Call it again if you need it.]"))]);

            // The stub is not free, so what left the context is the difference rather than the
            // whole of what was there. Both halves move: what is gone is added to the running
            // pruned total, and what remains stays counted as a tool result.
            var after = messages[entry.MessageIndex].Contents
                .OfType<FunctionResultContent>()
                .Sum(result => ResultTextOf(result).Length);

            _toolResultCharacters -= before - after;
            _prunedCharacters += before - after;
        }
    }

    /// <summary>
    /// The text of one tool result. Results are written as strings by <c>DispatchAsync</c>; the
    /// fallback exists because <see cref="FunctionResultContent.Result"/> is typed as
    /// <see cref="object"/> and a null one is a valid, if empty, result.
    /// </summary>
    private static string ResultTextOf(FunctionResultContent content) =>
        content.Result as string ?? string.Empty;

    /// <summary>
    /// Measures the part of every request that does not change from turn to turn: the system
    /// prompt, the response schema and the tool definitions.
    /// <para>
    /// This is the fixed toll a run pays on <i>every</i> turn — for the analysis prompt, roughly
    /// seven thousand tokens before a single changed-file row — which is exactly why the repository
    /// insists it be measured rather than eyeballed. Recomputed only when the structured-output
    /// mode changes, because that is the only thing that alters it.
    /// </para>
    /// </summary>
    private void MeasurePreamble(LlmConversation conversation, StructuredOutputMode mode)
    {
        _instructionCharacters = conversation.SystemPrompt.Length;

        _toolDefinitionCharacters = conversation.Tools.Sum(tool =>
            tool.Name.Length + tool.Description.Length + tool.ParametersSchemaJson.Length);

        if (conversation.ResponseFormat is not { } format)
        {
            _schemaCharacters = 0;
            return;
        }

        // The schema reaches the model twice in the stronger modes: once appended to the system
        // prompt, and once as the response format or as the submit tool's parameters. Both copies
        // are paid for on every turn, so both are counted.
        var inTheRequest = mode is StructuredOutputMode.Native or StructuredOutputMode.ToolCall
            ? format.SchemaJson.Length
            : 0;

        _schemaCharacters = StructuredOutput.PromptSuffix(format, mode).Length + inTheRequest;
    }

    /// <summary>
    /// Adds what the model just said to the running totals. Assistant messages are never pruned,
    /// so what goes in here stays in the context for the rest of the run.
    /// </summary>
    private void MeasureAssistant(IList<ChatMessage> produced)
    {
        foreach (var content in produced.SelectMany(message => message.Contents))
        {
            switch (content)
            {
                case TextContent text:
                    _assistantCharacters += text.Text.Length;
                    break;

                case FunctionCallContent call:
                    _assistantCharacters += call.Name.Length
                        + (call.Arguments is null ? 0 : JsonSerializer.Serialize(call.Arguments).Length);
                    break;

                case TextReasoningContent reasoning:
                    // Seen once is enough to know this provider surfaces reasoning at all, which is
                    // the difference between reporting zero and reporting nothing.
                    _reasoningReported = true;
                    _reasoningCharacters += reasoning.Text.Length;
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>The breakdown as it stands, for an event about to be raised.</summary>
    private LlmContextBreakdown ContextSnapshot() => new()
    {
        InstructionCharacters = _instructionCharacters,
        SchemaCharacters = _schemaCharacters,
        ToolDefinitionCharacters = _toolDefinitionCharacters,
        UserCharacters = _userCharacters,
        ToolResultCharacters = _toolResultCharacters,
        PrunedCharacters = _prunedCharacters,
        AssistantCharacters = _assistantCharacters,
        ReasoningCharacters = _reasoningReported ? _reasoningCharacters : null,
    };

    /// <summary>
    /// The input tokens of the request just sent, as the provider counted them — the honest answer
    /// to "how full is the context right now".
    /// <para>
    /// Null when no request has reported usage. A provider that reports none leaves this null for
    /// the whole run, and the meter says the size is unknown rather than showing a zero that would
    /// read as an empty context.
    /// </para>
    /// </summary>
    private long? ContextTokens()
    {
        for (var i = _requestUsages.Count - 1; i >= 0; i--)
        {
            if (_requestUsages[i].IsReported)
            {
                return _requestUsages[i].InputTokens;
            }
        }

        return null;
    }

    /// <summary>
    /// The model's context window: the profile's override first, then the bundled catalogue, then
    /// unknown.
    /// <para>
    /// Resolution order is exactly the one cost already uses, and unknown genuinely means unknown —
    /// the meter then shows an absolute count with no percentage rather than measuring against a
    /// guess. Nothing here decides whether a run may continue; see
    /// <see cref="LlmProviderProfile.ContextWindowTokens"/>.
    /// </para>
    /// </summary>
    private int? ContextWindow()
    {
        if (_profile.ContextWindowOverride is { } overridden)
        {
            return overridden;
        }

        return _pricing.TryGetContextWindow(_profile.ProviderType, _profile.Model, out var tokens)
            ? tokens
            : null;
    }

    /// <summary>
    /// Answers a submitted <c>submit_result</c> tool call before the conversation continues past
    /// it, when there was one.
    /// <para>
    /// <see cref="StructuredOutputMode.ToolCall"/>'s answer arrives as a tool call this session
    /// intercepts and reads as the final answer rather than dispatches — so nothing ever responds
    /// to it the way a real tool call is answered. That is invisible when the answer is accepted:
    /// the run just ends there. A repair round does not end there; it appends a further message
    /// on the same turn, and an assistant message whose tool call was never answered followed by
    /// a new message is a malformed request on any provider that enforces the pairing strictly —
    /// DeepSeek's default mode among them, which is why a repair round could 400 on the very next
    /// request even though nothing else about the conversation changed.
    /// </para>
    /// </summary>
    private void AcknowledgeSubmission(List<ChatMessage> messages, FunctionCallContent? submission)
    {
        if (submission is null)
        {
            return;
        }

        const string acknowledgement = "Received; a correction was requested next.";

        messages.Add(new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent(submission.CallId, acknowledgement)]));

        // Small, but it is a tool result in the transcript like any other, and the breakdown is
        // only trustworthy if every message it contains is in exactly one category.
        _toolResultCharacters += acknowledgement.Length;
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

    /// <summary>
    /// Runs the caller's own check over an answer that already satisfies the schema.
    /// <para>
    /// Deliberately not wrapped in a try/catch. A validator that throws is our bug, and the
    /// alternative to it surfacing is accepting a result nothing checked.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> CallerRejections(LlmConversation conversation, string answer) =>
        conversation.ResultValidator is { } validate ? validate(answer) : [];

    /// <summary>
    /// What is said when the answer was well-formed but wrong.
    /// <para>
    /// Unlike <see cref="RepairPrompt"/> this one <i>invites</i> tool calls. A schema violation
    /// is a formatting mistake the model can fix from what it already wrote; a rejected result
    /// usually means something in the repository was missed, and fixing that honestly means
    /// going back and looking.
    /// </para>
    /// </summary>
    private static string ResultRepairPrompt(IReadOnlyList<string> rejections) =>
        $"""
         Your answer matched the schema but failed the checks it has to pass:

         {string.Join(Environment.NewLine, rejections.Take(30).Select(rejection => "- " + rejection))}

         Fix exactly these and send the whole document again. Your tools are still available —
         read whatever you need first, because a problem fixed by looking is fixed properly.

         Do not delete anything to make a check pass, and do not invent anything either. A file
         with no node gets a real one; a reference to a missing id gets corrected, not removed.
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
        ResultRepairs = _resultRepairs,
    };

    /// <param name="structuredJson">
    /// The answer that was rejected, where there was one. Carried on a failure so the caller can
    /// show the user what it actually got — a result that kept failing the caller's own rules is
    /// worth seeing, unlike a transport error where there is nothing to show.
    /// </param>
    private LlmRunResult Failed(LlmFailure failure, int turn, string? structuredJson = null) => new()
    {
        // A context overflow is its own outcome, not a failure code with a label. Iteration 7
        // has to branch on it (requirement 6), and that reads better as an outcome than as a
        // string comparison at the call site.
        Outcome = failure.FailureCode == LlmFailures.ContextOverflow
            ? LlmRunOutcome.ContextOverflow
            : LlmRunOutcome.Failed,
        FailureCode = failure.FailureCode,
        ProviderMessage = failure.ProviderMessage,
        StructuredJson = structuredJson,
        Usage = _cumulative,
        TurnCount = turn,
        ToolCalls = _toolCalls,
        ResultRepairs = _resultRepairs,
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
            ResultRepairs = _resultRepairs,
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

    /// <summary>Where one turn's tool results live in <c>messages</c>, for <see cref="PruneOldToolResults"/>.</summary>
    private sealed record ToolMessageEntry(int MessageIndex, IReadOnlyList<(string CallId, string ToolName)> Calls);

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

    [LoggerMessage(
        EventId = 4009,
        Level = LogLevel.Warning,
        Message = "Provider {ProfileId} returned a schema-valid result the caller rejected for "
            + "{RejectionCount} reason(s); repair round {Round}.")]
    private static partial void ResultRejected(
        ILogger logger,
        string profileId,
        int rejectionCount,
        int round);
}
