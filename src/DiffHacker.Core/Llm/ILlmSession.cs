namespace DiffHacker.Core.Llm;

/// <summary>
/// One multi-turn, tool-using conversation with a model.
/// <para>
/// This is the whole of the LLM surface the rest of the application sees. Everything that
/// differs between providers — tool-call encoding, token accounting, error semantics, how
/// structured output is expressed, what counts as retryable — is absorbed behind it (§0.2.4),
/// and none of the types in this namespace mention <c>Microsoft.Extensions.AI</c>, which an
/// architecture test enforces (§0.3).
/// </para>
/// <para>
/// A session is per-run and single-use: create, <see cref="RunAsync"/>, read the usage,
/// dispose. It is not thread-safe and is not meant to be shared.
/// </para>
/// </summary>
public interface ILlmSession : IAsyncDisposable
{
    /// <summary>Everything consumed so far, including requests that failed or were retried.</summary>
    LlmUsage CumulativeUsage { get; }

    /// <summary>One entry per request to the provider, in order.</summary>
    IReadOnlyList<LlmUsage> RequestUsages { get; }

    /// <summary>Every tool call so far, in the order the model made them.</summary>
    IReadOnlyList<LlmToolCallRecord> ToolCalls { get; }

    /// <summary>
    /// Runs the conversation to completion, dispatching tool calls as the model asks for them.
    /// </summary>
    /// <param name="conversation">The prompt, the tools and the required answer shape.</param>
    /// <param name="progress">
    /// Optional observer of per-turn and per-tool-call events. Called on the running thread, so
    /// implementations must not block in it.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the run. The request chain unwinds and <see cref="OperationCanceledException"/> is
    /// thrown — but <see cref="CumulativeUsage"/>, <see cref="RequestUsages"/> and
    /// <see cref="ToolCalls"/> remain readable afterwards, which is how a cancelled run still
    /// reports what it spent.
    /// </param>
    /// <returns>
    /// How the run ended. Only cancellation throws; every other terminal condition, including
    /// a revoked key and a context overflow, comes back as an <see cref="LlmRunResult"/>.
    /// </returns>
    Task<LlmRunResult> RunAsync(
        LlmConversation conversation,
        IProgress<LlmRunEvent>? progress,
        CancellationToken cancellationToken);
}
