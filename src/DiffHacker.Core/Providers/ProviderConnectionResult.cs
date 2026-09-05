namespace DiffHacker.Core.Providers;

/// <summary>
/// Outcome of one real, authenticated request to a provider.
/// <para>
/// A failure is a result rather than an exception: the interface has to show a translated
/// headline (<see cref="FailureCode"/>) next to the provider's own wording
/// (<see cref="ProviderMessage"/>), and Iteration 2's requirement 5 is explicit that a
/// generic failure message will not do.
/// </para>
/// </summary>
public sealed record ProviderConnectionResult
{
    public required bool Succeeded { get; init; }

    /// <summary>Models the provider reported. Empty when it exposes no listing endpoint.</summary>
    public IReadOnlyList<string> AvailableModels { get; init; } = [];

    /// <summary>Stable, lower_snake_case failure category. Null when <see cref="Succeeded"/>.</summary>
    public string? FailureCode { get; init; }

    /// <summary>
    /// The provider's own error text. Scrubbed of the API key at the RPC boundary before it
    /// crosses the bridge — providers do sometimes echo the key back.
    /// </summary>
    public string? ProviderMessage { get; init; }

    public int? HttpStatus { get; init; }

    public static ProviderConnectionResult Success(IReadOnlyList<string> models) =>
        new() { Succeeded = true, AvailableModels = models };

    public static ProviderConnectionResult Failure(string failureCode, string? providerMessage, int? httpStatus = null) =>
        new()
        {
            Succeeded = false,
            FailureCode = failureCode,
            ProviderMessage = providerMessage,
            HttpStatus = httpStatus,
        };
}

/// <summary>
/// The failure categories <see cref="ProviderConnectionResult.FailureCode"/> uses. Each has a
/// matching key in the renderer's string catalogue.
/// </summary>
public static class ProviderConnectionFailures
{
    public const string InvalidKey = "provider_invalid_key";
    public const string Forbidden = "provider_forbidden";
    public const string QuotaExhausted = "provider_quota_exhausted";
    public const string RateLimited = "provider_rate_limited";
    public const string EndpointNotFound = "provider_endpoint_not_found";
    public const string Unreachable = "provider_unreachable";
    public const string TimedOut = "provider_timed_out";
    public const string UnexpectedResponse = "provider_unexpected_response";
}
