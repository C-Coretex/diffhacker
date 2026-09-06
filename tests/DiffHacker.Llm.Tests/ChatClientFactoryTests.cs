using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Requirement 2: all five providers, plus the generic OpenAI-compatible type, reachable
/// through one contract.
/// <para>
/// The endpoint each one is given is the whole of the difference between them, and two of the
/// six are not simply "the base URL from the settings form". Gemini's native API is not
/// OpenAI-shaped, so it goes to Google's compatible surface at <c>/v1beta/openai/</c>; the
/// Anthropic SDK appends its own <c>/v1</c>, so it must not be handed one. Both rules apply to
/// a user-supplied override as well, which is what these tests are really pinning.
/// </para>
/// </summary>
public sealed class ChatClientFactoryTests
{
    [Theory]
    [InlineData(LlmProviderType.OpenAi, "https://api.openai.com/v1")]
    [InlineData(LlmProviderType.Grok, "https://api.x.ai/v1")]
    [InlineData(LlmProviderType.DeepSeek, "https://api.deepseek.com")]
    [InlineData(LlmProviderType.Gemini, "https://generativelanguage.googleapis.com/v1beta/openai/")]
    [InlineData(LlmProviderType.Anthropic, "https://api.anthropic.com")]
    public void Each_provider_is_pointed_at_its_own_endpoint(LlmProviderType type, string expected)
    {
        ChatClientFactory.ResolveBaseUrl(SessionHarness.ProfileFor(type)).ShouldBe(expected);
    }

    [Fact]
    public void A_configured_base_url_overrides_the_default()
    {
        var profile = SessionHarness.ProfileFor(
            LlmProviderType.OpenAiCompatible,
            baseUrl: "http://127.0.0.1:11434/v1");

        // The local-Ollama case, and the cheapest way to prove the compatible path for real.
        ChatClientFactory.ResolveBaseUrl(profile).ShouldBe("http://127.0.0.1:11434/v1");
    }

    [Fact]
    public void A_Gemini_profile_pointed_at_the_native_endpoint_still_reaches_the_compatible_one()
    {
        // Someone who typed the native URL into the settings form meant "talk to Gemini here",
        // not "talk to Gemini in a dialect it does not speak".
        var profile = SessionHarness.ProfileFor(
            LlmProviderType.Gemini,
            baseUrl: "https://generativelanguage.googleapis.com/v1beta");

        ChatClientFactory.ResolveBaseUrl(profile)
            .ShouldBe("https://generativelanguage.googleapis.com/v1beta/openai/");
    }

    [Fact]
    public void A_Gemini_profile_already_pointed_at_the_compatible_endpoint_is_left_alone()
    {
        var profile = SessionHarness.ProfileFor(
            LlmProviderType.Gemini,
            baseUrl: "https://gateway.example.test/v1beta/openai/");

        ChatClientFactory.ResolveBaseUrl(profile).ShouldBe("https://gateway.example.test/v1beta/openai/");
    }

    [Fact]
    public void An_Anthropic_proxy_typed_with_a_version_segment_still_works()
    {
        // The SDK appends its own /v1/messages. Handed one, the request would go to /v1/v1/….
        var profile = SessionHarness.ProfileFor(
            LlmProviderType.Anthropic,
            baseUrl: "https://proxy.example.test/v1");

        ChatClientFactory.ResolveBaseUrl(profile).ShouldBe("https://proxy.example.test");
    }

    [Fact]
    public void An_OpenAI_compatible_profile_without_a_base_url_is_refused_before_any_request()
    {
        var profile = SessionHarness.ProfileFor(LlmProviderType.OpenAiCompatible);

        var thrown = Should.Throw<LlmConfigurationException>(
            () => ChatClientFactory.ResolveBaseUrl(profile));

        thrown.FailureCode.ShouldBe("provider_base_url_required");
    }

    [Fact]
    public void A_nonsense_base_url_is_refused_rather_than_attempted()
    {
        var profile = SessionHarness.ProfileFor(LlmProviderType.OpenAiCompatible, baseUrl: "not a url");

        using var httpClient = new HttpClient();
        var thrown = Should.Throw<LlmConfigurationException>(
            () => ChatClientFactory.Create(profile, "sk-test-key", httpClient));

        thrown.FailureCode.ShouldBe("provider_invalid_base_url");
    }

    [Theory]
    [InlineData(LlmProviderType.OpenAi)]
    [InlineData(LlmProviderType.Anthropic)]
    [InlineData(LlmProviderType.Gemini)]
    [InlineData(LlmProviderType.Grok)]
    [InlineData(LlmProviderType.DeepSeek)]
    public void Every_provider_type_produces_a_working_client(LlmProviderType type)
    {
        // Construction only — nothing here reaches the network, and requirement 9 forbids it.
        using var httpClient = new HttpClient();
        using var client = ChatClientFactory.Create(SessionHarness.ProfileFor(type), "sk-test-key", httpClient);

        client.ShouldNotBeNull();
    }

    [Fact]
    public void The_generic_compatible_type_produces_a_working_client_too()
    {
        using var httpClient = new HttpClient();
        var profile = SessionHarness.ProfileFor(
            LlmProviderType.OpenAiCompatible,
            model: "llama3.1",
            baseUrl: "http://127.0.0.1:11434/v1");

        using var client = ChatClientFactory.Create(profile, "ollama", httpClient);

        client.ShouldNotBeNull();
    }
}
