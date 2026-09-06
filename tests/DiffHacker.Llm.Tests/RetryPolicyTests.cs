using System.Net;
using DiffHacker.Core.Llm;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Requirement 5's other half: rate limits and transient failures back off and retry, and
/// everything else does not.
/// <para>
/// Nothing here waits. The curve is asserted from the delays the policy returns, because a
/// retry test that actually slept would take half a minute and would still fail on a loaded
/// machine for reasons that have nothing to do with the policy.
/// </para>
/// </summary>
public sealed class RetryPolicyTests
{
    private const int MaxAttempts = 5;

    [Fact]
    public void A_rate_limit_backs_off_exponentially()
    {
        var failure = Failure(429);

        // Jitter of 1.0 is the top of each window, which is where the nominal curve lives.
        var delays = Enumerable.Range(1, MaxAttempts)
            .Select(attempt => RetryPolicy.NextDelay(failure, attempt, MaxAttempts, jitter: 1.0))
            .ToArray();

        delays.Select(delay => delay!.Value.TotalSeconds).ShouldBe([1, 2, 4, 8, 16]);
    }

    [Fact]
    public void Jitter_spreads_the_wait_without_collapsing_it()
    {
        var failure = Failure(429);

        var earliest = RetryPolicy.NextDelay(failure, 3, MaxAttempts, jitter: 0.0)!.Value;
        var latest = RetryPolicy.NextDelay(failure, 3, MaxAttempts, jitter: 1.0)!.Value;

        // Half the window to all of it: enough spread that two runs starting together do not
        // retry in lockstep, never so little as to hammer the provider.
        earliest.TotalSeconds.ShouldBe(2);
        latest.TotalSeconds.ShouldBe(4);
    }

    [Fact]
    public void The_attempts_are_capped()
    {
        RetryPolicy.NextDelay(Failure(429), MaxAttempts + 1, MaxAttempts, jitter: 0.5).ShouldBeNull();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void A_non_transient_failure_is_never_retried(int status)
    {
        RetryPolicy.NextDelay(Failure(status), 1, MaxAttempts, jitter: 0.5).ShouldBeNull(
            "a revoked key retried five times is five identical rejections and thirty wasted seconds.");
    }

    [Fact]
    public void The_providers_own_Retry_After_beats_the_curve()
    {
        var failure = ProviderErrorMapper.Classify(
            FakeProviderResponse.OpenAiStyle(429, "slow down", retryAfter: "7"));

        failure.RetryAfter.ShouldBe(TimeSpan.FromSeconds(7));
        RetryPolicy.NextDelay(failure, 1, MaxAttempts, jitter: 1.0)!.Value.ShouldBe(
            TimeSpan.FromSeconds(7),
            "the provider knows when it will accept traffic again; the curve is only a guess.");
    }

    [Fact]
    public void A_Retry_After_date_is_understood_as_well_as_a_count_of_seconds()
    {
        var when = DateTimeOffset.UtcNow.AddSeconds(30).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        var failure = ProviderErrorMapper.Classify(
            FakeProviderResponse.OpenAiStyle(429, "slow down", retryAfter: when));

        failure.RetryAfter.ShouldNotBeNull();
        failure.RetryAfter!.Value.TotalSeconds.ShouldBeInRange(25, 31);
    }

    [Fact]
    public void An_absurd_Retry_After_stops_the_run_rather_than_hanging_it()
    {
        var failure = Failure(429) with { RetryAfter = TimeSpan.FromMinutes(10) };

        RetryPolicy.NextDelay(failure, 1, MaxAttempts, jitter: 0.5).ShouldBeNull(
            "a ten-minute wait is indistinguishable from a hung application; say so instead.");
    }

    [Fact]
    public void No_single_wait_exceeds_the_ceiling()
    {
        var failure = Failure(429);

        RetryPolicy.NextDelay(failure, 12, maxAttempts: 20, jitter: 1.0)!.Value
            .ShouldBeLessThanOrEqualTo(RetryPolicy.MaxDelay);
    }

    private static LlmFailure Failure(int status) =>
        ProviderErrorMapper.Classify(FakeProviderResponse.OpenAiStyle(status, string.Empty));

    [Fact]
    public async Task The_session_retries_a_rate_limit_and_then_succeeds()
    {
        // The policy is only useful if the loop actually applies it.
        var harness = new SessionHarness();
        harness.Provider
            .ThrowsRepeatedly(() => FakeProviderResponse.OpenAiStyle(429, "slow down"), times: 2)
            .Says("done");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            harness.Progress,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        harness.Delays.Count.ShouldBe(2);
        harness.Events.Count(e => e.Kind == LlmRunEventKind.RetryScheduled).ShouldBe(2);
        harness.Events
            .First(e => e.Kind == LlmRunEventKind.RetryScheduled)
            .ReasonCode.ShouldBe(LlmFailures.RateLimited);
    }

    [Fact]
    public async Task The_session_does_not_retry_a_revoked_key()
    {
        var harness = new SessionHarness();
        harness.Provider.Throws(FakeProviderResponse.OpenAiStyle((int)HttpStatusCode.Unauthorized, "bad key"));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Failed);
        result.FailureCode.ShouldBe(LlmFailures.InvalidKey);
        harness.Delays.ShouldBeEmpty("nothing was waited for, because nothing could have changed.");
        harness.Provider.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_failure_that_outlasts_every_retry_is_reported_with_its_own_code()
    {
        var harness = new SessionHarness();
        harness.Provider.ThrowsRepeatedly(
            () => FakeProviderResponse.OpenAiStyle(429, "slow down"),
            times: MaxAttempts + 1);

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            null,
            TestContext.Current.CancellationToken);

        result.FailureCode.ShouldBe(LlmFailures.RateLimited);
        harness.Delays.Count.ShouldBe(MaxAttempts);
    }
}
