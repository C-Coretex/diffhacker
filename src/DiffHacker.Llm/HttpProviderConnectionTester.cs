using System.Net;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Llm;

/// <summary>
/// Proves credentials work by listing the models the key can reach.
/// <para>
/// Every provider DiffHacker supports exposes a model listing, and on all of them it is free:
/// no tokens are consumed, so there is nothing to warn the user about. It also answers a
/// question a completion cannot — whether the free-text model identifier the user typed is one
/// this key can actually use — and supplies the suggestions requirement 4 asks for without a
/// hardcoded list.
/// </para>
/// </summary>
public sealed partial class HttpProviderConnectionTester(
    HttpClient httpClient,
    ILogger<HttpProviderConnectionTester> logger)
    : IProviderConnectionTester
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async ValueTask<ProviderConnectionResult> TestAsync(
        LlmProviderProfile profile,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var baseUrl = profile.BaseUrl ?? ProviderEndpoints.DefaultBaseUrl(profile.ProviderType);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return ProviderConnectionResult.Failure(
                ProviderConnectionFailures.EndpointNotFound,
                "No endpoint is configured for this provider.");
        }

        if (!Uri.TryCreate(CombineUrl(baseUrl, ProviderEndpoints.ModelsPath), UriKind.Absolute, out var uri))
        {
            return ProviderConnectionResult.Failure(
                ProviderConnectionFailures.EndpointNotFound,
                $"'{baseUrl}' is not a valid absolute URL.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ProviderEndpoints.Authenticate(request, profile.ProviderType, apiKey);

            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ProviderConnectionResult.Success(ModelListParser.Parse(body));
            }

            TestFailed(logger, profile.Id, (int)response.StatusCode);

            return ProviderConnectionResult.Failure(
                CategoriseStatus(response.StatusCode),
                Summarise(body),
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderConnectionResult.Failure(
                ProviderConnectionFailures.TimedOut,
                $"No response within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            // DNS failure, connection refused, TLS problem: the request never reached a
            // provider, so there is no status code and no provider wording to show.
            return ProviderConnectionResult.Failure(ProviderConnectionFailures.Unreachable, ex.Message);
        }
    }

    private static string CombineUrl(string baseUrl, string path) =>
        baseUrl.TrimEnd('/') + "/" + path;

    private static string CategoriseStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => ProviderConnectionFailures.InvalidKey,
        HttpStatusCode.Forbidden => ProviderConnectionFailures.Forbidden,
        HttpStatusCode.PaymentRequired => ProviderConnectionFailures.QuotaExhausted,
        HttpStatusCode.TooManyRequests => ProviderConnectionFailures.RateLimited,
        HttpStatusCode.NotFound => ProviderConnectionFailures.EndpointNotFound,
        _ => ProviderConnectionFailures.UnexpectedResponse,
    };

    /// <summary>
    /// Caps the provider's response so a stray HTML error page does not become the whole
    /// interface. The key is scrubbed out of this at the RPC boundary, not here.
    /// </summary>
    private static string Summarise(string body)
    {
        const int limit = 600;
        var trimmed = body.Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[..limit] + "…";
    }

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Connection test for provider {ProfileId} failed with HTTP {Status}.")]
    private static partial void TestFailed(ILogger logger, string profileId, int status);
}
