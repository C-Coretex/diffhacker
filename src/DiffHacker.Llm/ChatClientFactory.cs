using System.ClientModel;
using Anthropic;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DiffHacker.Llm;

/// <summary>
/// Turns a configured provider profile into an <see cref="IChatClient"/>.
/// <para>
/// Six provider types, two SDKs. Anthropic has its own wire format and its own official
/// package; everything else — OpenAI, Grok, DeepSeek, Gemini and whatever endpoint a user
/// typed in — speaks the OpenAI format, so they share one client and differ only in the base
/// URL. Gemini is in that group because Google publishes an OpenAI-compatible surface; see
/// <see cref="ProviderEndpoints.ChatBaseUrl"/>.
/// </para>
/// <para>
/// Nothing above this class knows any of that, which is the point of §0.2.4.
/// </para>
/// </summary>
internal static class ChatClientFactory
{
    /// <summary>
    /// Builds a client for <paramref name="profile"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The transport. Ownership stays with the caller — neither SDK is asked to dispose it,
    /// and <c>LlmSessionFactory</c> gives each session its own so a finished run cannot take
    /// the next one's connections with it.
    /// </param>
    /// <exception cref="LlmConfigurationException">
    /// The profile has no usable endpoint. Only reachable for an OpenAI-compatible profile
    /// saved without a base URL, which the settings form already refuses.
    /// </exception>
    public static IChatClient Create(LlmProviderProfile profile, string apiKey, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(httpClient);

        var baseUrl = ResolveBaseUrl(profile);

        return profile.ProviderType == LlmProviderType.Anthropic
            ? CreateAnthropic(profile, apiKey, baseUrl, httpClient)
            : CreateOpenAiCompatible(profile, apiKey, baseUrl, httpClient);
    }

    /// <summary>
    /// The endpoint a conversation with <paramref name="profile"/> goes to.
    /// <para>
    /// A base URL on the profile always wins, then gets the same normalisation the default
    /// would: a user who pointed a Gemini profile at the native endpoint still reaches the
    /// compatible one, and an Anthropic proxy typed with a trailing <c>/v1</c> still works.
    /// </para>
    /// </summary>
    public static string ResolveBaseUrl(LlmProviderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            return ProviderEndpoints.ChatBaseUrl(profile.ProviderType)
                ?? throw new LlmConfigurationException(
                    "provider_base_url_required",
                    $"Profile '{profile.Id}' has no base URL and its provider type has no default.");
        }

        return profile.ProviderType switch
        {
            LlmProviderType.Gemini => ProviderEndpoints.GeminiCompatibleSuffix(profile.BaseUrl),
            LlmProviderType.Anthropic => ProviderEndpoints.WithoutApiVersion(profile.BaseUrl),
            _ => profile.BaseUrl,
        };
    }

    private static IChatClient CreateAnthropic(
        LlmProviderProfile profile,
        string apiKey,
        string baseUrl,
        HttpClient httpClient)
    {
        // The official SDK ships the IChatClient itself, including tool calling, structured
        // output and usage reporting, so there is no adapter of ours between here and MEAI.
        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            BaseUrl = baseUrl,
            HttpClient = httpClient,

            // The SDK retries transient failures on its own. Left on, every attempt this
            // layer makes would silently be several, so the trace and the retry events would
            // both be lies. RetryPolicy is the one place backoff happens.
            MaxRetries = 0,
        };

        return client.AsIChatClient(profile.Model);
    }

    private static IChatClient CreateOpenAiCompatible(
        LlmProviderProfile profile,
        string apiKey,
        string baseUrl,
        HttpClient httpClient)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new LlmConfigurationException(
                "provider_invalid_base_url",
                $"'{baseUrl}' is not a valid absolute URL.");
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient),
        };

        return new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(profile.Model)
            .AsIChatClient();
    }
}
