using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using Anthropic.Exceptions;
using DiffHacker.Core.Llm;

namespace DiffHacker.Llm;

/// <summary>
/// One failed request, classified.
/// </summary>
/// <param name="FailureCode">A <see cref="LlmFailures"/> code.</param>
/// <param name="ProviderMessage">The provider's own wording, capped. Never null-empty prose.</param>
/// <param name="HttpStatus">The status, when there was a response at all.</param>
/// <param name="IsTransient">Whether trying again could plausibly work.</param>
/// <param name="RetryAfter">The provider's own instruction on when to try again, if it gave one.</param>
internal readonly record struct LlmFailure(
    string FailureCode,
    string? ProviderMessage,
    int? HttpStatus,
    bool IsTransient,
    TimeSpan? RetryAfter);

/// <summary>
/// Turns whatever a provider threw into one of a dozen codes a person can act on.
/// <para>
/// Requirement 5 asks for <b>distinct, actionable</b> errors, and this is where that is won or
/// lost. A revoked key, a mistyped model name, an exhausted balance and a prompt that outgrew
/// the context window arrive as broadly similar HTTP failures, and they lead to four entirely
/// different fixes. Collapsing them into "the request failed" would make the layer above
/// useless.
/// </para>
/// <para>
/// Three of the classes are not visible in the status code at all — context overflow, an
/// unknown model and a content filter — so the body is inspected for them first. That is
/// string matching against provider prose, which is unlovely and will need maintenance; the
/// alternative is not distinguishing them, which requirement 6 rules out because Iteration 7
/// has to react to an overflow specifically.
/// </para>
/// </summary>
internal static class ProviderErrorMapper
{
    /// <summary>
    /// Caps the provider's wording so a stray HTML error page does not become the whole
    /// interface. Matches <see cref="HttpProviderConnectionTester"/>'s limit.
    /// </summary>
    private const int MessageLimit = 600;

    public static LlmFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // The Anthropic SDK's own hierarchy carries both halves we need.
            AnthropicApiException api => FromResponse((int)api.StatusCode, api.ResponseBody, api.Message),
            AnthropicIOException io => Unreachable(io),

            // The OpenAI SDK (and therefore Grok, DeepSeek, Gemini and every compatible
            // endpoint) reports everything as one exception type with the body in Message.
            ClientResultException client => FromResponse(client.Status, client.Message, client.Message)
                with
            { RetryAfter = RetryAfterOf(client) },

            // Only a timeout reaches here. The session checks the caller's token first, so a
            // user's cancellation is rethrown rather than being classified as a failure.
            OperationCanceledException => new LlmFailure(
                LlmFailures.TimedOut, null, null, IsTransient: true, RetryAfter: null),

            HttpRequestException or SocketException or IOException => Unreachable(exception),

            _ => new LlmFailure(
                LlmFailures.UnexpectedResponse,
                Summarise(exception.Message),
                null,
                IsTransient: false,
                RetryAfter: null),
        };
    }

    /// <summary>
    /// Classifies a response that arrived successfully but is not usable — a content filter, or
    /// a truncation that means the answer is incomplete.
    /// </summary>
    public static string? ClassifyFinishReason(string? finishReason)
    {
        if (string.IsNullOrEmpty(finishReason))
        {
            return null;
        }

        if (Contains(finishReason, "content_filter", "safety", "refusal", "prohibited"))
        {
            return LlmFailures.ContentFilter;
        }

        // A hit output limit is not a failure of this layer: the loop simply has no tool call
        // and no complete answer, and the schema validation that follows says so more usefully.
        return null;
    }

    private static LlmFailure Unreachable(Exception exception) => new(
        LlmFailures.Unreachable,
        Summarise(exception.Message),
        null,

        // A connection that never established is exactly the kind of failure a retry fixes.
        IsTransient: true,
        RetryAfter: null);

    private static LlmFailure FromResponse(int status, string? body, string? fallbackMessage)
    {
        var message = Summarise(string.IsNullOrWhiteSpace(body) ? fallbackMessage : body);
        var haystack = (body ?? string.Empty) + " " + (fallbackMessage ?? string.Empty);

        // Body first: these three are invisible in the status code, and two of them arrive
        // wearing a status code that means something else entirely (400 and 429).
        if (Contains(haystack,
                "context_length_exceeded",
                "maximum context length",
                "context window",
                "prompt is too long",
                "input length and `max_tokens` exceed",
                "too many total text bytes",
                "request_too_large"))
        {
            return new LlmFailure(LlmFailures.ContextOverflow, message, status, false, null);
        }

        if (Contains(haystack,
                "model_not_found",
                "model not found",
                "unknown model",
                "invalid model",
                "does not exist or you do not have access",
                "is not a valid model"))
        {
            return new LlmFailure(LlmFailures.ModelNotFound, message, status, false, null);
        }

        if (Contains(haystack,
                "content_filter",
                "content_policy",
                "prohibited_content",
                "responsible_ai",
                "safety_settings",
                "blocked by the safety"))
        {
            return new LlmFailure(LlmFailures.ContentFilter, message, status, false, null);
        }

        // "You exceeded your current quota" arrives as a 429, which would otherwise be read as
        // rate limiting and retried five times for nothing.
        if (Contains(haystack,
                "insufficient_quota",
                "exceeded your current quota",
                "credit balance is too low",
                "billing_not_active",
                "no credits"))
        {
            return new LlmFailure(LlmFailures.QuotaExhausted, message, status, false, null);
        }

        return FromStatus(status, message);
    }

    private static LlmFailure FromStatus(int status, string? message) => status switch
    {
        (int)HttpStatusCode.Unauthorized =>
            new LlmFailure(LlmFailures.InvalidKey, message, status, false, null),

        (int)HttpStatusCode.Forbidden =>
            new LlmFailure(LlmFailures.Forbidden, message, status, false, null),

        (int)HttpStatusCode.PaymentRequired =>
            new LlmFailure(LlmFailures.QuotaExhausted, message, status, false, null),

        // On a completions endpoint a 404 is the model, not the URL: the base URL was already
        // proved by the connection test that saved this profile.
        (int)HttpStatusCode.NotFound =>
            new LlmFailure(LlmFailures.ModelNotFound, message, status, false, null),

        (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.GatewayTimeout =>
            new LlmFailure(LlmFailures.TimedOut, message, status, true, null),

        (int)HttpStatusCode.TooManyRequests =>
            new LlmFailure(LlmFailures.RateLimited, message, status, true, null),

        // 529 is Anthropic's "overloaded", which is not in HttpStatusCode.
        >= 500 and < 600 =>
            new LlmFailure(LlmFailures.UnexpectedResponse, message, status, true, null),

        _ => new LlmFailure(LlmFailures.UnexpectedResponse, message, status, false, null),
    };

    /// <summary>
    /// The provider's own <c>Retry-After</c>, which always beats our backoff curve. Both
    /// spellings are in use: seconds, and an HTTP date.
    /// </summary>
    private static TimeSpan? RetryAfterOf(ClientResultException exception)
    {
        var response = exception.GetRawResponse();
        if (response is null || !response.Headers.TryGetValue("Retry-After", out var value) || value is null)
        {
            return null;
        }

        if (int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
        }

        if (DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var when))
        {
            var delay = when - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string? Summarise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= MessageLimit ? trimmed : trimmed[..MessageLimit] + "…";
    }
}
