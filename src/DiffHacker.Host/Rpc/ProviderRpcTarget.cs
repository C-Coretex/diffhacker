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

        var rate = ReadRate(request);

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

            // Only meaningful as a pair: half a rate would bill the other half at zero, which
            // is a wrong number presented as an authoritative one.
            InputCostPerMillion = rate?.Input,
            OutputCostPerMillion = rate?.Output,

            // Unlike the pair above, this stands alone: half a rate is a wrong number, half a
            // context window is not a thing. Absent clears the override and falls back to the
            // bundled table, exactly as an absent rate does.
            ContextWindowTokens = ReadContextWindow(request),

            // The same shape again: each stands alone, and absent clears the override and falls
            // back to LlmBudget.Default.
            MaxToolCallsOverride = ReadMaxToolCalls(request),
            MaxTotalTokensOverride = ReadMaxTotalTokens(request),
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

    /// <summary>
    /// Tests exactly what is currently in the provider form, saved or not.
    /// <para>
    /// A profile named by <see cref="TestConnectionRequest.Id"/> supplies the stored key when
    /// <see cref="TestConnectionRequest.ApiKey"/> is blank — the same "blank means unchanged"
    /// convention <c>providers.save</c> follows — but every other field comes from the request,
    /// because an edited-but-unsaved type, model or base URL is what the reviewer is asking
    /// about, not what was last saved.
    /// </para>
    /// </summary>
    [JsonRpcMethod("providers.testConnection")]
    public async Task<TestConnectionResult> TestConnectionAsync(TestConnectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var providerType = ProviderTypeWire.ToDomain(request.ProviderType);
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

        var apiKey = request.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) && existing is not null)
        {
            apiKey = await secrets
                .GetAsync(LlmProviderProfile.SecretName(existing.Id), cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw RpcErrors.Failure("provider_key_missing", "No API key was entered to test.");
        }

        var model = request.Model?.Trim() ?? string.Empty;
        var now = DateTimeOffset.UtcNow;
        var candidate = new LlmProviderProfile
        {
            Id = existing?.Id ?? "unsaved",
            ProviderType = providerType,
            DisplayName = existing?.DisplayName ?? string.Empty,
            Model = model,
            BaseUrl = baseUrl,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now,
        };

        var result = await tester.TestAsync(candidate, apiKey, cancellationToken).ConfigureAwait(false);

        var models = result.AvailableModels;
        var verified = model.Length > 0
            && models.Count > 0
            && models.Contains(model, StringComparer.OrdinalIgnoreCase);

        if (result.Succeeded && models.Count > 0 && existing is not null)
        {
            // The provider's own list is the only model catalogue in the product. Requirement 4
            // rules out a hardcoded one, and this is where the suggestions come from instead.
            // Nothing to cache onto when the profile has not been saved yet — the response's own
            // availableModels is what the form uses for suggestions until then.
            await profiles
                .SaveAsync(existing with { ModelSuggestions = models, UpdatedAtUtc = now }, cancellationToken)
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
            inputCostPerMillion: (double?)profile.InputCostPerMillion,
            isActive: isActive,
            maxToolCalls: profile.MaxToolCallsOverride,
            maxTotalTokens: profile.MaxTotalTokensOverride,
            model: profile.Model,
            modelSuggestions: profile.ModelSuggestions,
            contextWindowTokens: profile.ContextWindowTokens,
            outputCostPerMillion: (double?)profile.OutputCostPerMillion,
            providerType: ProviderTypeWire.ToWire(profile.ProviderType));

    /// <summary>
    /// The optional price override, accepted only as a complete pair.
    /// <para>
    /// The bundled price table is a snapshot and goes stale, so this is how a user on a model
    /// it has never heard of still gets a cost estimate. Half of one, though, is worse than
    /// none: it would bill the missing side at zero and present the result as a real number.
    /// </para>
    /// </summary>
    private static (decimal Input, decimal Output)? ReadRate(SaveProviderRequest request)
    {
        if (request.InputCostPerMillion is not { } input || request.OutputCostPerMillion is not { } output)
        {
            return null;
        }

        if (input < 0 || output < 0)
        {
            throw RpcErrors.Failure(
                "provider_invalid_cost",
                "A token price cannot be negative.");
        }

        return ((decimal)input, (decimal)output);
    }

    /// <summary>
    /// The optional context-window override, for the same reason the price override exists: the
    /// bundled table is a snapshot, and a user on a model it has never heard of should be able to
    /// say how big the window is rather than be told forever that it is unknown.
    /// <para>
    /// Rejected rather than clamped when it is not positive. A context window of zero or less is
    /// not a smaller window, it is a typo, and silently turning it into "no override" would leave
    /// the user looking at the table's number wondering why theirs did not take.
    /// </para>
    /// </summary>
    private static int? ReadContextWindow(SaveProviderRequest request)
    {
        if (request.ContextWindowTokens is not { } tokens)
        {
            return null;
        }

        if (tokens <= 0)
        {
            throw RpcErrors.Failure(
                "provider_invalid_context_window",
                "A context window must be a positive number of tokens.");
        }

        return tokens;
    }

    /// <summary>
    /// The optional tool-call budget override. Rejected rather than clamped when not positive,
    /// for the same reason as <see cref="ReadContextWindow"/>: a typo silently becoming "no
    /// override" would leave the user looking at the default wondering why theirs did not take.
    /// </summary>
    private static int? ReadMaxToolCalls(SaveProviderRequest request)
    {
        if (request.MaxToolCalls is not { } calls)
        {
            return null;
        }

        if (calls <= 0)
        {
            throw RpcErrors.Failure(
                "provider_invalid_max_tool_calls",
                "A tool-call limit must be a positive number.");
        }

        return calls;
    }

    /// <summary>The optional token budget override. See <see cref="ReadMaxToolCalls"/>.</summary>
    private static long? ReadMaxTotalTokens(SaveProviderRequest request)
    {
        if (request.MaxTotalTokens is not { } tokens)
        {
            return null;
        }

        if (tokens <= 0)
        {
            throw RpcErrors.Failure(
                "provider_invalid_max_total_tokens",
                "A token limit must be a positive number.");
        }

        return tokens;
    }
}
