using DiffHacker.Contracts;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Secrets;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Logging;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Configuring LLM providers, and testing that the credentials work.
/// <para>
/// <b>The key rule of this file:</b> an API key travels in exactly one direction. It arrives on
/// <c>providers.save</c>, goes straight into the secret store, and no method here ever puts one
/// into a response (CLAUDE.md §0.2.13). <see cref="ProviderProfile"/> carries
/// <c>hasApiKey</c> and nothing more.
/// </para>
/// </summary>
public sealed class ProviderRpcTarget(
    IProviderProfileStore profiles,
    ISecretStore secrets,
    IProviderConnectionTester tester,
    ILogger<ProviderRpcTarget> logger)
{
    [JsonRpcMethod("providers.list")]
    public async Task<ProviderProfileList> ListAsync(CancellationToken cancellationToken)
    {
        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        var activeId = await profiles.GetActiveIdAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<ProviderProfile>(stored.Count);
        foreach (var profile in stored)
        {
            var hasKey = await secrets
                .ContainsAsync(LlmProviderProfile.SecretName(profile.Id), cancellationToken)
                .ConfigureAwait(false);

            results.Add(ToWire(profile, hasKey, profile.Id == activeId));
        }

        return new ProviderProfileList(activeId, results);
    }

    [JsonRpcMethod("providers.save")]
    public async Task<ProviderProfileList> SaveAsync(SaveProviderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerType = ProviderTypeWire.ToDomain(request.ProviderType);

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw RpcErrors.Failure("provider_model_required", "A model identifier is required.");
        }

        var baseUrl = Normalise(request.BaseUrl);

        if (providerType is LlmProviderType.OpenAiCompatible && baseUrl is null)
        {
            throw RpcErrors.Failure(
                "provider_base_url_required",
                "An OpenAI-compatible endpoint needs a base URL; there is nothing to infer it from.");
        }

        if (baseUrl is not null && !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw RpcErrors.Failure("provider_invalid_base_url", $"'{baseUrl}' is not an absolute URL.");
        }

        var existing = string.IsNullOrEmpty(request.Id)
            ? null
            : await profiles.FindAsync(request.Id, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(request.Id) && existing is null)
        {
            throw RpcErrors.Failure("provider_not_found", $"No provider profile with id '{request.Id}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var profile = new LlmProviderProfile
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("n"),
            ProviderType = providerType,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? ProviderTypeNames.ToStorage(providerType)
                : request.DisplayName.Trim(),
            Model = request.Model.Trim(),
            BaseUrl = baseUrl,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
            // Preserved across an edit: suggestions come from the last successful test, and
            // renaming a profile should not throw them away.
            ModelSuggestions = existing?.ModelSuggestions ?? [],
        };

        await profiles.SaveAsync(profile, cancellationToken).ConfigureAwait(false);

        // An absent apiKey on an update means "leave the stored key alone", which is what lets
        // the form edit a profile without the key ever being sent back to it first.
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            await secrets
                .SetAsync(LlmProviderProfile.SecretName(profile.Id), request.ApiKey, cancellationToken)
                .ConfigureAwait(false);
        }

        // First profile configured becomes the active one; otherwise the user chooses.
        if (await profiles.GetActiveIdAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            await profiles.SetActiveIdAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Saved provider profile {ProfileId} ({ProviderType})",
            profile.Id,
            ProviderTypeNames.ToStorage(profile.ProviderType));

        return await ListAsync(cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("providers.delete")]
    public async Task<ProviderProfileList> DeleteAsync(ProviderIdRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await profiles.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
        await secrets.DeleteAsync(LlmProviderProfile.SecretName(request.Id), cancellationToken).ConfigureAwait(false);

        if (await profiles.GetActiveIdAsync(cancellationToken).ConfigureAwait(false) == request.Id)
        {
            var remaining = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
            await profiles
                .SetActiveIdAsync(remaining.Count > 0 ? remaining[0].Id : null, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ListAsync(cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("providers.setActive")]
    public async Task<ProviderProfileList> SetActiveAsync(ProviderIdRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await profiles.FindAsync(request.Id, cancellationToken).ConfigureAwait(false) is null)
        {
            throw RpcErrors.Failure("provider_not_found", $"No provider profile with id '{request.Id}'.");
        }

        await profiles.SetActiveIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        return await ListAsync(cancellationToken).ConfigureAwait(false);
    }

    [JsonRpcMethod("providers.testConnection")]
    public async Task<TestConnectionResult> TestConnectionAsync(ProviderIdRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await profiles.FindAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw RpcErrors.Failure("provider_not_found", $"No provider profile with id '{request.Id}'.");

        var apiKey = await secrets
            .GetAsync(LlmProviderProfile.SecretName(profile.Id), cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(apiKey))
        {
            throw RpcErrors.Failure("provider_key_missing", $"No API key is stored for profile '{profile.Id}'.");
        }

        var result = await tester.TestAsync(profile, apiKey, cancellationToken).ConfigureAwait(false);

        var models = result.AvailableModels;
        var verified = models.Count > 0
            && models.Contains(profile.Model, StringComparer.OrdinalIgnoreCase);

        if (result.Succeeded && models.Count > 0)
        {
            // The provider's own list is the only model catalogue in the product. Requirement 4
            // rules out a hardcoded one, and this is where the suggestions come from instead.
            await profiles
                .SaveAsync(profile with { ModelSuggestions = models, UpdatedAtUtc = DateTimeOffset.UtcNow }, cancellationToken)
                .ConfigureAwait(false);
        }

        return new TestConnectionResult(
            availableModels: models,
            failureCode: result.FailureCode,
            httpStatus: result.HttpStatus,
            modelVerified: result.Succeeded && verified,
            providerMessage: Scrub(result.ProviderMessage, apiKey),
            succeeded: result.Succeeded);
    }

    /// <summary>
    /// Providers do sometimes echo the key back in an error body, so it is removed by exact
    /// match as well as by the usual shape-based patterns before this crosses the bridge.
    /// </summary>
    private static string? Scrub(string? message, string apiKey)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var withoutKey = message.Replace(apiKey, SecretRedactor.Placeholder, StringComparison.Ordinal);
        return SecretRedactor.Scrub(withoutKey);
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');

    private static ProviderProfile ToWire(LlmProviderProfile profile, bool hasApiKey, bool isActive) =>
        new(
            baseUrl: profile.BaseUrl,
            displayName: profile.DisplayName,
            hasApiKey: hasApiKey,
            id: profile.Id,
            isActive: isActive,
            model: profile.Model,
            modelSuggestions: profile.ModelSuggestions,
            providerType: ProviderTypeWire.ToWire(profile.ProviderType));
}
