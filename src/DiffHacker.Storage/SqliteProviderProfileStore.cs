using System.Text.Json;
using Dapper;
using DiffHacker.Core.Providers;
using DiffHacker.Core.Settings;

namespace DiffHacker.Storage;

/// <summary>
/// SQLite-backed provider configuration.
/// <para>
/// There is no API key column and never will be. Keys go to <c>ISecretStore</c>; this table
/// holds only what is safe to read with a text editor.
/// </para>
/// </summary>
public sealed class SqliteProviderProfileStore(AppDatabase database) : IProviderProfileStore
{
    private const string ActiveProfileKey = "active_provider_id";

    private const string SelectColumns =
        """
        SELECT id                AS Id,
               provider_type     AS ProviderType,
               display_name      AS DisplayName,
               model             AS Model,
               base_url          AS BaseUrl,
               created_at_utc    AS CreatedAtUtc,
               updated_at_utc    AS UpdatedAtUtc,
               model_suggestions AS ModelSuggestions
        FROM provider_profiles
        """;

    public async ValueTask<IReadOnlyList<LlmProviderProfile>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ProviderProfileRow>(new CommandDefinition(
            SelectColumns + " ORDER BY display_name COLLATE NOCASE;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(static row => row.ToProfile())];
    }

    public async ValueTask<LlmProviderProfile?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<ProviderProfileRow>(new CommandDefinition(
            SelectColumns + " WHERE id = @id;",
            new { id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToProfile();
    }

    public async ValueTask SaveAsync(LlmProviderProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO provider_profiles
                (id, provider_type, display_name, model, base_url,
                 created_at_utc, updated_at_utc, model_suggestions)
            VALUES (@Id, @ProviderType, @DisplayName, @Model, @BaseUrl,
                    @CreatedAtUtc, @UpdatedAtUtc, @ModelSuggestions)
            ON CONFLICT(id) DO UPDATE SET
                provider_type     = @ProviderType,
                display_name      = @DisplayName,
                model             = @Model,
                base_url          = @BaseUrl,
                updated_at_utc    = @UpdatedAtUtc,
                model_suggestions = @ModelSuggestions;
            """,
            ProviderProfileRow.From(profile),
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM provider_profiles WHERE id = @id;",
            new { id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask<string?> GetActiveIdAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var value = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM app_settings WHERE key = @key;",
            new { key = ActiveProfileKey },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return string.IsNullOrEmpty(value) ? null : value;
    }

    public async ValueTask SetActiveIdAsync(string? id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = id is null
            ? "DELETE FROM app_settings WHERE key = @key;"
            : """
              INSERT INTO app_settings (key, value) VALUES (@key, @value)
              ON CONFLICT(key) DO UPDATE SET value = @value;
              """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { key = ActiveProfileKey, value = id },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// The persisted row shape. Dapper binds by name in both directions, so this doubles as the
    /// parameter object for the upsert.
    /// </summary>
    private sealed record ProviderProfileRow
    {
        public required string Id { get; init; }

        public required string ProviderType { get; init; }

        public required string DisplayName { get; init; }

        public required string Model { get; init; }

        public string? BaseUrl { get; init; }

        public required string CreatedAtUtc { get; init; }

        public required string UpdatedAtUtc { get; init; }

        /// <summary>A JSON array. §0.3 chose SQLite with JSON documents plus indexed columns.</summary>
        public string? ModelSuggestions { get; init; }

        public static ProviderProfileRow From(LlmProviderProfile profile) => new()
        {
            Id = profile.Id,
            ProviderType = ProviderTypeNames.ToStorage(profile.ProviderType),
            DisplayName = profile.DisplayName,
            Model = profile.Model,
            BaseUrl = profile.BaseUrl,
            CreatedAtUtc = Timestamps.Format(profile.CreatedAtUtc),
            UpdatedAtUtc = Timestamps.Format(profile.UpdatedAtUtc),
            ModelSuggestions = profile.ModelSuggestions.Count == 0
                ? null
                : JsonSerializer.Serialize(profile.ModelSuggestions, StorageJson.Options),
        };

        public LlmProviderProfile ToProfile() => new()
        {
            Id = Id,
            ProviderType = ProviderTypeNames.FromStorage(ProviderType),
            DisplayName = DisplayName,
            Model = Model,
            BaseUrl = BaseUrl,
            CreatedAtUtc = Timestamps.Parse(CreatedAtUtc),
            UpdatedAtUtc = Timestamps.Parse(UpdatedAtUtc),
            ModelSuggestions = ModelSuggestions is null
                ? []
                : JsonSerializer.Deserialize<List<string>>(ModelSuggestions, StorageJson.Options) ?? [],
        };
    }
}

internal static class StorageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
