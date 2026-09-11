using DiffHacker.Contracts;
using DiffHacker.Core.Llm;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Carries the mechanical traffic of a live run — which tool, with what arguments, for how long,
/// and what came back — to the renderer as <c>analysis.toolCall</c> notifications.
/// <para>
/// A second method beside <c>analysis.progress</c> rather than more kinds on the first one. The
/// two carry different things to different places: progress is the model's own sentence about what
/// it is doing and belongs at the top of the screen, and this is a log the reviewer scrolls when
/// they want to know what it actually did. Interleaving them would put a hundred rows of
/// <c>read_file</c> between two sentences a person was reading.
/// </para>
/// <para>
/// <see cref="IProgress{T}"/> is called on the running thread and must not block it, so this
/// starts the notification and does not await it. A notification that fails is logged and dropped:
/// nothing about a live view is worth failing a run over.
/// </para>
/// </summary>
public sealed partial class RunEventNotifier(IRpcNotifier notifier, ILogger<RunEventNotifier> logger)
    : IProgress<LlmRunEvent>
{
    /// <summary>The notification method name. Mirrored in the renderer's RpcNotifications.</summary>
    public const string Method = "analysis.toolCall";

    private int _sequence;

    public void Report(LlmRunEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (ToKind(value.Kind) is not { } kind)
        {
            return;
        }

        var context = value.Context;

        var payload = new ToolCallEvent(
            argumentsPreview: value.ArgumentsPreview,
            // Stamped here rather than carried on LlmRunEvent: this is when the event is observed
            // on the way to the renderer, the same boundary that already assigns sequence.
            atUtc: DateTime.UtcNow,
            // The provider's own count of the last request, and the window to measure it against.
            // Both stay null when they are unknown: a zero here would read as an empty context, and
            // a guessed window would make a meter that is confidently wrong.
            contextTokens: value.ContextTokens is { } occupied
                ? (int)Math.Min(occupied, int.MaxValue)
                : null,
            contextWindowTokens: value.ContextWindowTokens,

            // The breakdown is ours and exact, in characters. Absent on the events raised before a
            // request was ever built, which is why every one of these is nullable.
            contextInstructionsCharacters: context?.InstructionCharacters,
            contextSchemaCharacters: context?.SchemaCharacters,
            contextToolDefinitionCharacters: context?.ToolDefinitionCharacters,
            contextOpeningCharacters: context?.UserCharacters,
            contextToolResultCharacters: context?.ToolResultCharacters,
            contextPrunedCharacters: context?.PrunedCharacters,
            contextAssistantCharacters: context?.AssistantCharacters,
            contextReasoningCharacters: context?.ReasoningCharacters,
            costUsd: value.CumulativeUsage.EstimatedCostUsd is { } cost ? (double)cost : null,
            durationMs: value.Duration?.TotalMilliseconds,
            // Token counts are long in the domain and int on the wire. A run that overflowed an
            // int would have hit the budget's two-million-token stop a thousand times over, so the
            // saturating cast is a formality rather than a lossy conversion.
            inputTokens: (int)Math.Min(value.CumulativeUsage.InputTokens, int.MaxValue),
            isError: value.IsError,
            kind: kind,
            outputTokens: (int)Math.Min(value.CumulativeUsage.OutputTokens, int.MaxValue),
            reasonCode: value.ReasonCode,
            reasoningText: value.ReasoningText,
            responseText: value.ResponseText,
            resultBytes: value.ResultBytes,
            resultPreview: value.ResultPreview,
            retryAttempt: value.RetryAttempt,
            retryDelayMs: value.RetryDelay?.TotalMilliseconds,
            sequence: Interlocked.Increment(ref _sequence),
            toolCallCount: value.ToolCallCount,
            toolName: value.ToolName,
            turn: value.Turn);

        _ = NotifyAsync(payload);
    }

    private async Task NotifyAsync(ToolCallEvent payload)
    {
        try
        {
            await notifier.NotifyAsync(Method, payload, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Nothing about a live view is worth failing a run over.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            EventUndelivered(logger, ex);
        }
    }

    /// <summary>
    /// Maps the run's event kinds onto the wire enum.
    /// <para>
    /// <c>TurnFinished</c> is deliberately dropped: it carries nothing the following
    /// <c>TurnStarted</c> or the usage update does not, and every event costs a notification.
    /// </para>
    /// </summary>
    private static ToolCallEventKind? ToKind(LlmRunEventKind kind) => kind switch
    {
        LlmRunEventKind.TurnStarted => ToolCallEventKind.Turn_started,
        LlmRunEventKind.ToolCallStarted => ToolCallEventKind.Tool_started,
        LlmRunEventKind.ToolCallFinished => ToolCallEventKind.Tool_finished,
        LlmRunEventKind.RetryScheduled => ToolCallEventKind.Retry,
        LlmRunEventKind.UsageUpdated => ToolCallEventKind.Usage,
        LlmRunEventKind.AssistantMessage => ToolCallEventKind.Assistant_message,
        _ => null,
    };

    [LoggerMessage(
        EventId = 6020,
        Level = LogLevel.Debug,
        Message = "A run event could not be delivered to the renderer.")]
    private static partial void EventUndelivered(ILogger logger, Exception exception);
}
