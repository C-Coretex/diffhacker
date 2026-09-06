namespace DiffHacker.Core.Llm;

/// <summary>
/// Why a run stopped, as a stable lower_snake_case code.
/// <para>
/// Iteration 4's requirement 5 is that a failure must be <b>distinct and actionable</b>: "the
/// key is revoked" and "the model does not exist" lead a user to two different fixes, and a
/// layer that collapses them into "the request failed" has thrown away the only useful part.
/// Each code has a matching key in the renderer's string catalogue, the same arrangement
/// <see cref="Providers.ProviderConnectionFailures"/> uses.
/// </para>
/// </summary>
public static class LlmFailures
{
    /// <summary>The provider rejected the credentials outright.</summary>
    public const string InvalidKey = "llm_invalid_key";

    /// <summary>Authenticated, but not permitted to use this model or endpoint.</summary>
    public const string Forbidden = "llm_forbidden";

    /// <summary>The model identifier is not one this key can reach.</summary>
    public const string ModelNotFound = "llm_model_not_found";

    /// <summary>
    /// The conversation no longer fits the model's context window. Reported as
    /// <see cref="LlmRunOutcome.ContextOverflow"/> rather than a generic failure, because
    /// Iteration 7 has to react to it rather than merely display it.
    /// </summary>
    public const string ContextOverflow = "llm_context_overflow";

    /// <summary>The provider's safety system refused the request or the response.</summary>
    public const string ContentFilter = "llm_content_filter";

    /// <summary>Credit or quota exhausted. Distinct from rate limiting: waiting will not help.</summary>
    public const string QuotaExhausted = "llm_quota_exhausted";

    /// <summary>Rate limited after every retry was spent.</summary>
    public const string RateLimited = "llm_rate_limited";

    /// <summary>Nothing answered: DNS, connection refused, TLS.</summary>
    public const string Unreachable = "llm_unreachable";

    /// <summary>The provider accepted the request but did not answer in time.</summary>
    public const string TimedOut = "llm_timed_out";

    /// <summary>
    /// The model answered, but not in the shape it was asked for, and the repair attempt did
    /// not fix it. A provider problem rather than a transport one.
    /// </summary>
    public const string InvalidResponse = "llm_invalid_response";

    /// <summary>A hard stop from <see cref="LlmBudget"/> fired.</summary>
    public const string BudgetExceeded = "llm_budget_exceeded";

    /// <summary>Anything not recognised. The provider's own wording carries the detail.</summary>
    public const string UnexpectedResponse = "llm_unexpected_response";

    /// <summary>
    /// Every code above, for tests that assert the taxonomy and the string catalogue agree.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        InvalidKey,
        Forbidden,
        ModelNotFound,
        ContextOverflow,
        ContentFilter,
        QuotaExhausted,
        RateLimited,
        Unreachable,
        TimedOut,
        InvalidResponse,
        BudgetExceeded,
        UnexpectedResponse,
    ];
}
