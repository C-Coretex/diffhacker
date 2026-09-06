namespace DiffHacker.Llm;

/// <summary>
/// Exponential backoff for the failures that are worth trying again.
/// <para>
/// Deliberately small and hand-written rather than a resilience package. What it has to do is
/// narrow — five attempts, a doubling delay, honour <c>Retry-After</c>, and never retry a
/// failure that a retry cannot fix — and all of that has to be observable, because Iteration
/// 13 shows retries in the live run view. A pipeline library would hide the third requirement
/// behind its own abstractions to save writing the first two.
/// </para>
/// <para>
/// The distinction that matters is <see cref="LlmFailure.IsTransient"/>. A revoked key retried
/// five times is five identical rejections and thirty wasted seconds; a 429 not retried at all
/// is a run that fails for no reason. <see cref="ProviderErrorMapper"/> decides which is which.
/// </para>
/// </summary>
internal static class RetryPolicy
{
    /// <summary>First delay. Doubles each attempt: 1, 2, 4, 8, 16 seconds.</summary>
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ceiling on one wait, including a <c>Retry-After</c>. A provider asking for ten minutes
    /// is asking for a run that looks hung; stop instead and say why.
    /// </summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to wait before <paramref name="attempt"/> (1-based), or null when this failure
    /// must not be retried or the attempts are spent.
    /// </summary>
    /// <param name="failure">The classified failure that just happened.</param>
    /// <param name="attempt">Which retry this would be, from 1.</param>
    /// <param name="maxAttempts">The budget's ceiling.</param>
    /// <param name="jitter">
    /// A number in [0, 1). Injected rather than taken from <c>Random.Shared</c> so a test can
    /// assert the curve exactly, and so two runs starting together do not retry in lockstep.
    /// </param>
    public static TimeSpan? NextDelay(LlmFailure failure, int attempt, int maxAttempts, double jitter)
    {
        if (!failure.IsTransient || attempt > maxAttempts || attempt < 1)
        {
            return null;
        }

        // The provider knows better than the curve does.
        if (failure.RetryAfter is { } stated)
        {
            return stated > MaxDelay ? null : stated;
        }

        var exponential = BaseDelay * Math.Pow(2, attempt - 1);

        // Full jitter over the exponential window. Spreading the retries matters more than
        // hitting the nominal delay, which is why this is not a small perturbation of it.
        var jittered = TimeSpan.FromMilliseconds(exponential.TotalMilliseconds * (0.5 + (jitter * 0.5)));

        return jittered > MaxDelay ? MaxDelay : jittered;
    }

    /// <summary>Jitter drawn from the shared generator. Split out so tests can supply their own.</summary>
    public static double Jitter() => Random.Shared.NextDouble();
}
