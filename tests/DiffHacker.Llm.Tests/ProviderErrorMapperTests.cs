using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using Anthropic.Exceptions;
using DiffHacker.Core.Llm;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Requirement 5: non-transient errors surface as <b>distinct, actionable</b> messages, and
/// requirement 6: context overflow is a typed condition rather than an opaque failure.
/// <para>
/// This is the file that earns those. A revoked key, a mistyped model name, an exhausted
/// balance and a prompt that outgrew the window arrive as broadly similar HTTP failures — some
/// of them wearing each other's status codes — and they lead to four different fixes. If they
/// collapse into one code here, nothing downstream can un-collapse them.
/// </para>
/// </summary>
public sealed class ProviderErrorMapperTests
{
    [Theory]
    [InlineData(401, "", LlmFailures.InvalidKey)]
    [InlineData(403, "", LlmFailures.Forbidden)]
    [InlineData(402, "", LlmFailures.QuotaExhausted)]
    [InlineData(404, "", LlmFailures.ModelNotFound)]
    [InlineData(408, "", LlmFailures.TimedOut)]
    [InlineData(429, "", LlmFailures.RateLimited)]
    [InlineData(500, "", LlmFailures.UnexpectedResponse)]
    [InlineData(400, "This model's maximum context length is 128000 tokens", LlmFailures.ContextOverflow)]
    [InlineData(400, "prompt is too long: 210000 tokens > 200000", LlmFailures.ContextOverflow)]
    [InlineData(404, "The model `gpt-9` does not exist or you do not have access to it", LlmFailures.ModelNotFound)]
    [InlineData(400, "The response was filtered due to content_filter triggering", LlmFailures.ContentFilter)]
    [InlineData(429, "You exceeded your current quota, please check your plan", LlmFailures.QuotaExhausted)]
    public void Each_failure_class_maps_to_a_distinct_code(int status, string body, string expected)
    {
        var failure = ProviderErrorMapper.Classify(OpenAiStyle(status, body));

        failure.FailureCode.ShouldBe(expected);
        failure.HttpStatus.ShouldBe(status);
    }

    [Fact]
    public void The_twelve_failure_codes_are_all_distinct()
    {
        // The taxonomy is the contract: two codes with the same string would silently merge two
        // different fixes into one message.
        LlmFailures.All.Distinct(StringComparer.Ordinal).Count().ShouldBe(LlmFailures.All.Count);
    }

    [Theory]
    [InlineData(429, "", true)]
    [InlineData(408, "", true)]
    [InlineData(500, "", true)]
    [InlineData(529, "overloaded", true)]
    [InlineData(401, "", false)]
    [InlineData(403, "", false)]
    [InlineData(404, "", false)]
    [InlineData(400, "maximum context length", false)]
    [InlineData(429, "You exceeded your current quota", false)]
    public void Only_failures_a_retry_could_fix_are_transient(int status, string body, bool expected)
    {
        ProviderErrorMapper.Classify(OpenAiStyle(status, body)).IsTransient.ShouldBe(
            expected,
            "retrying a revoked key five times is five identical rejections; not retrying a 429 fails for no reason.");
    }

    [Fact]
    public void A_quota_message_wearing_a_429_is_not_mistaken_for_rate_limiting()
    {
        // The trap this file exists for. OpenAI reports an exhausted balance as 429, which read
        // as rate limiting would be retried five times, wait thirty seconds, and still fail.
        var failure = ProviderErrorMapper.Classify(
            OpenAiStyle(429, """{"error":{"code":"insufficient_quota","message":"You exceeded your current quota"}}"""));

        failure.FailureCode.ShouldBe(LlmFailures.QuotaExhausted);
        failure.IsTransient.ShouldBeFalse();
    }

    [Fact]
    public void The_Anthropic_hierarchy_is_read_through_its_own_exception_types()
    {
        var failure = ProviderErrorMapper.Classify(FakeProviderResponse.AnthropicStyle(
            HttpStatusCode.Unauthorized,
            """{"error":{"type":"authentication_error","message":"invalid x-api-key"}}"""));

        failure.FailureCode.ShouldBe(LlmFailures.InvalidKey);
        failure.HttpStatus.ShouldBe(401);
        failure.ProviderMessage.ShouldNotBeNull();
        failure.ProviderMessage!.ShouldContain("invalid x-api-key");
    }

    [Fact]
    public void Anthropics_prompt_length_error_is_a_context_overflow_not_a_bad_request()
    {
        var failure = ProviderErrorMapper.Classify(FakeProviderResponse.AnthropicStyle(
            HttpStatusCode.BadRequest,
            """{"error":{"type":"invalid_request_error","message":"prompt is too long: 250000 tokens > 200000 maximum"}}"""));

        failure.FailureCode.ShouldBe(
            LlmFailures.ContextOverflow,
            "Iteration 7 has to react to this specifically, so it cannot arrive as a generic 400.");
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(SocketException))]
    public void Nothing_answering_is_distinguished_from_being_rejected(Type exceptionType)
    {
        var exception = exceptionType == typeof(SocketException)
            ? new SocketException((int)SocketError.HostNotFound)
            : (Exception)new HttpRequestException("No such host is known.");

        var failure = ProviderErrorMapper.Classify(exception);

        failure.FailureCode.ShouldBe(LlmFailures.Unreachable);
        failure.HttpStatus.ShouldBeNull("Nothing answered, so there is no status to report.");
        failure.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public void An_Anthropic_transport_failure_is_unreachable_rather_than_unexpected()
    {
        var failure = ProviderErrorMapper.Classify(
            new AnthropicIOException("connection reset", new HttpRequestException("reset")));

        failure.FailureCode.ShouldBe(LlmFailures.Unreachable);
    }

    [Fact]
    public void A_providers_own_wording_survives_to_the_caller()
    {
        // Requirement 5 asks for the actual error. Flattening it into "the request failed" is
        // exactly the loss this layer is supposed to prevent.
        var failure = ProviderErrorMapper.Classify(
            OpenAiStyle(401, """{"error":{"message":"Incorrect API key provided: sk-te***"}}"""));

        failure.ProviderMessage.ShouldNotBeNull();
        failure.ProviderMessage!.ShouldContain("Incorrect API key provided");
    }

    [Fact]
    public void A_wall_of_provider_text_is_capped()
    {
        var failure = ProviderErrorMapper.Classify(OpenAiStyle(500, new string('x', 5_000)));

        failure.ProviderMessage!.Length.ShouldBeLessThan(
            700,
            "a stray HTML error page must not become the whole interface.");
    }

    [Fact]
    public void A_content_filter_is_recognised_in_a_finish_reason_too()
    {
        // Not every refusal is an exception: some providers answer 200 and say so in the
        // finish reason.
        ProviderErrorMapper.ClassifyFinishReason("content_filter").ShouldBe(LlmFailures.ContentFilter);
        ProviderErrorMapper.ClassifyFinishReason("stop").ShouldBeNull();
        ProviderErrorMapper.ClassifyFinishReason(null).ShouldBeNull();
    }

    [Fact]
    public void An_unrecognised_exception_is_reported_rather_than_swallowed()
    {
        var failure = ProviderErrorMapper.Classify(new InvalidTimeZoneException("something odd"));

        failure.FailureCode.ShouldBe(LlmFailures.UnexpectedResponse);
        failure.IsTransient.ShouldBeFalse("guessing that an unknown failure is retryable burns money.");
    }

    private static ClientResultException OpenAiStyle(int status, string body) =>
        FakeProviderResponse.OpenAiStyle(status, body);
}
