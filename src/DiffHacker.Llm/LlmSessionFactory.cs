using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Llm;

/// <summary>
/// Builds a session for a configured provider.
/// <para>
/// The API key is fetched here and never leaves: it goes into the SDK client and into nothing
/// else. No caller above this line is given one, which is the arrangement that has kept
/// §0.2.13 true so far.
/// </para>
/// </summary>
public sealed partial class LlmSessionFactory(
    ISecretStore secrets,
    ITokenPricing pricing,
    ILoggerFactory loggerFactory) : ILlmSessionFactory
{
    public async ValueTask<ILlmSession> CreateAsync(
        LlmProviderProfile profile,
        LlmBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(budget);

        var apiKey = await secrets
            .GetAsync(LlmProviderProfile.SecretName(profile.Id), cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new LlmConfigurationException(
                "provider_key_missing",
                $"No API key is stored for profile '{profile.Id}'.");
        }

        // One transport per session, owned by it. The application's shared HttpClient is not
        // used: both SDKs are given the client and neither documents whether it disposes one,
        // and a finished run closing the connection pool out from under the settings screen
        // would be a genuinely baffling bug to chase.
        var httpClient = new HttpClient();

        try
        {
            var chat = ChatClientFactory.Create(profile, apiKey, httpClient);

            SessionCreated(
                loggerFactory.CreateLogger<LlmSessionFactory>(),
                profile.Id,
                ProviderTypeNames.ToStorage(profile.ProviderType),
                profile.Model);

            return new LlmSession(
                chat,
                httpClient,
                profile,
                budget,
                pricing,
                loggerFactory.CreateLogger<LlmSession>());
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    [LoggerMessage(
        EventId = 4009,
        Level = LogLevel.Information,
        Message = "Opened an LLM session on provider {ProfileId} ({ProviderType}, model {Model}).")]
    private static partial void SessionCreated(
        ILogger logger, string profileId, string providerType, string model);
}
