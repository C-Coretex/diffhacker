namespace DiffHacker.Core.Providers;

/// <summary>
/// A configured LLM provider as it is persisted.
/// <para>
/// Deliberately carries no API key. Keys live only in <c>ISecretStore</c>, under the name
/// <see cref="SecretName"/> derives, so no accidental serialisation of this record can leak
/// one (CLAUDE.md §0.2.13).
/// </para>
/// </summary>
public sealed record LlmProviderProfile
{
    public required string Id { get; init; }

    public required LlmProviderType ProviderType { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Model identifier as free text. Never validated against a hardcoded list.</summary>
    public required string Model { get; init; }

    /// <summary>Endpoint override. Required for <see cref="LlmProviderType.OpenAiCompatible"/>.</summary>
    public string? BaseUrl { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>
    /// Models the last successful connection test reported. These are the suggestions the
    /// model field offers; there is no hardcoded list anywhere, because those rot.
    /// </summary>
    public IReadOnlyList<string> ModelSuggestions { get; init; } = [];

    /// <summary>Name this profile's API key is stored under in the secret store.</summary>
    public static string SecretName(string profileId) => "provider:" + profileId;
}
