using Dapper;
using DiffHacker.Core.Settings;

namespace DiffHacker.Storage;

/// <summary>
/// The <c>app_settings</c> key/value table, as a store anyone can use.
/// <para>
/// The table has existed since schema 1 and until now had exactly one consumer, reading and writing
/// <c>active_provider_id</c> from inside <see cref="SqliteProviderProfileStore"/>. Iteration 10 gave
/// it a second, so the access moved here rather than being copied there.
/// </para>
/// </summary>
public sealed class SqliteAppSettingStore(AppDatabase database) : IAppSettingStore
{
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var value = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM app_settings WHERE key = @key;",
            new { key },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return string.IsNullOrEmpty(value) ? null : value;
    }

    public async ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = string.IsNullOrEmpty(value)
            ? "DELETE FROM app_settings WHERE key = @key;"
            : """
              INSERT INTO app_settings (key, value) VALUES (@key, @value)
              ON CONFLICT(key) DO UPDATE SET value = @value;
              """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { key, value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
