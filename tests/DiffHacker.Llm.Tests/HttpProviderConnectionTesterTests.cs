using System.Net;
using DiffHacker.Core.Providers;
using DiffHacker.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Llm.Tests;

public sealed class HttpProviderConnectionTesterTests
{
    private const string ApiKey = "sk-test-abcdefghijklmnopqrstuvwxyz";

    private const string OpenAiStyleBody =
        """{"object":"list","data":[{"id":"gpt-4o","object":"model"},{"id":"gpt-4o-mini","object":"model"}]}""";

    private const string GeminiStyleBody =
        """{"models":[{"name":"models/gemini-2.5-pro"},{"name":"models/gemini-2.5-flash"}]}""";

    [Fact]
    public async Task A_successful_listing_reports_the_models()
    {
        var (tester, _) = Create(HttpStatusCode.OK, OpenAiStyleBody);

        var result = await tester.TestAsync(Profile(), ApiKey, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.AvailableModels.ShouldBe(["gpt-4o", "gpt-4o-mini"]);
        result.FailureCode.ShouldBeNull();
    }

    [Fact]
    public async Task Gemini_model_names_lose_their_models_prefix()
    {
        var (tester, _) = Create(HttpStatusCode.OK, GeminiStyleBody);

        var result = await tester.TestAsync(
            Profile(LlmProviderType.Gemini),
            ApiKey,
            TestContext.Current.CancellationToken);

        // Users type "gemini-2.5-pro", not "models/gemini-2.5-pro", so verification has to
        // compare like with like.
        result.AvailableModels.ShouldBe(["gemini-2.5-pro", "gemini-2.5-flash"]);
    }

    [Theory]
    [InlineData(LlmProviderType.OpenAi, "https://api.openai.com/v1/models", "Authorization")]
    [InlineData(LlmProviderType.Grok, "https://api.x.ai/v1/models", "Authorization")]
    [InlineData(LlmProviderType.DeepSeek, "https://api.deepseek.com/models", "Authorization")]
    [InlineData(LlmProviderType.Anthropic, "https://api.anthropic.com/v1/models", "x-api-key")]
    [InlineData(LlmProviderType.Gemini, "https://generativelanguage.googleapis.com/v1beta/models", "x-goog-api-key")]
    public async Task Each_provider_is_addressed_and_authenticated_its_own_way(
        LlmProviderType type,
        string expectedUrl,
        string expectedHeader)
    {
        var (tester, handler) = Create(HttpStatusCode.OK, OpenAiStyleBody);

        await tester.TestAsync(Profile(type), ApiKey, TestContext.Current.CancellationToken);

        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.RequestUri!.ToString().ShouldBe(expectedUrl);
        handler.LastRequest.Headers.Contains(expectedHeader).ShouldBeTrue();
    }

    [Fact]
    public async Task Anthropic_carries_its_required_version_header()
    {
        var (tester, handler) = Create(HttpStatusCode.OK, OpenAiStyleBody);

        await tester.TestAsync(
            Profile(LlmProviderType.Anthropic),
            ApiKey,
            TestContext.Current.CancellationToken);

        handler.LastRequest!.Headers.Contains("anthropic-version").ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderConnectionFailures.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, ProviderConnectionFailures.Forbidden)]
    [InlineData(HttpStatusCode.PaymentRequired, ProviderConnectionFailures.QuotaExhausted)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderConnectionFailures.RateLimited)]
    [InlineData(HttpStatusCode.NotFound, ProviderConnectionFailures.EndpointNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderConnectionFailures.UnexpectedResponse)]
    public async Task Each_failure_class_maps_to_a_distinct_code(HttpStatusCode status, string expected)
    {
        var (tester, _) = Create(status, """{"error":{"message":"nope"}}""");

        var result = await tester.TestAsync(Profile(), ApiKey, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBe(expected);
        result.HttpStatus.ShouldBe((int)status);
    }

    [Fact]
    public async Task The_providers_own_wording_is_preserved()
    {
        // Requirement 5 asks for the *actual* error, so this must survive to the interface
        // rather than being flattened into "the connection failed".
        var (tester, _) = Create(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided: sk-te***. You can find your API key at ..."}}""");

        var result = await tester.TestAsync(Profile(), ApiKey, TestContext.Current.CancellationToken);

        result.ProviderMessage.ShouldNotBeNull();
        result.ProviderMessage!.ShouldContain("Incorrect API key provided");
    }

    [Fact]
    public async Task An_unreachable_host_is_distinguished_from_a_rejected_key()
    {
        var (tester, _) = Create(
            HttpStatusCode.OK,
            string.Empty,
            new HttpRequestException("No such host is known."));

        var result = await tester.TestAsync(Profile(), ApiKey, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBe(ProviderConnectionFailures.Unreachable);
        result.HttpStatus.ShouldBeNull("Nothing answered, so there is no status to report.");
    }

    [Fact]
    public async Task An_OpenAI_compatible_endpoint_without_a_base_url_fails_before_any_request()
    {
        var (tester, handler) = Create(HttpStatusCode.OK, OpenAiStyleBody);

        var result = await tester.TestAsync(
            Profile(LlmProviderType.OpenAiCompatible),
            ApiKey,
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.FailureCode.ShouldBe(ProviderConnectionFailures.EndpointNotFound);
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task A_configured_base_url_overrides_the_provider_default()
    {
        var (tester, handler) = Create(HttpStatusCode.OK, OpenAiStyleBody);

        var profile = Profile() with { BaseUrl = "https://gateway.example.test/v1" };
        await tester.TestAsync(profile, ApiKey, TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://gateway.example.test/v1/models");
    }

    [Fact]
    public async Task A_response_that_is_not_a_model_list_is_success_with_no_models()
    {
        // Some OpenAI-compatible servers answer 200 with something else entirely. The key still
        // works, so this is not a failure — there is just nothing to suggest.
        var (tester, _) = Create(HttpStatusCode.OK, "not json at all");

        var result = await tester.TestAsync(Profile(), ApiKey, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.AvailableModels.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_API_key_never_appears_in_the_returned_message()
    {
        // The host scrubs this again at the RPC boundary, but a tester that echoed the key back
        // would be one careless change away from a leak.
        var (tester, _) = Create(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"bad request"}}""");

        var result = await tester.TestAsync(Profile(), ApiKey, TestContext.Current.CancellationToken);

        result.ProviderMessage.ShouldNotBeNull();
        result.ProviderMessage!.ShouldNotContain(ApiKey);
    }

    private static (HttpProviderConnectionTester Tester, StubHttpMessageHandler Handler) Create(
        HttpStatusCode status,
        string body,
        Exception? throwInstead = null)
    {
        var handler = new StubHttpMessageHandler(status, body, throwInstead);
        var tester = new HttpProviderConnectionTester(
            new HttpClient(handler),
            NullLogger<HttpProviderConnectionTester>.Instance);

        return (tester, handler);
    }

    private static LlmProviderProfile Profile(LlmProviderType type = LlmProviderType.OpenAi) => new()
    {
        Id = "p1",
        ProviderType = type,
        DisplayName = "Test",
        Model = "gpt-4o",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };
}
