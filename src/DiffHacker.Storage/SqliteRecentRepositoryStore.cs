using System.Globalization;
using Dapper;
using DiffHacker.Core.Repositories;
using DiffHacker.Core.Settings;

namespace DiffHacker.Storage;

/// <summary>SQLite-backed recent-repository list.</summary>
public sealed class SqliteRecentRepositoryStore(AppDatabase database) : IRecentRepositoryStore
{
    /// <summary>
    /// Enough to be useful without turning the welcome screen into a wall. Older entries fall
    /// off the end rather than accumulating forever.
    /// </summary>
    private const int MaxEntries = 20;

    public async ValueTask<IReadOnlyList<RecentRepository>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<RecentRepositoryRow>(new CommandDefinition(
            """
            SELECT path AS Path, name AS Name, last_opened_utc AS LastOpenedUtc
            FROM recent_repositories
            ORDER BY last_opened_utc DESC;
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(static row => new RecentRepository
        {
            Path = row.Path,
            Name = row.Name,
            LastOpenedUtc = Timestamps.Parse(row.LastOpenedUtc),
        })];
    }

    public async ValueTask RememberAsync(string path, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO recent_repositories (path, name, last_opened_utc)
            VALUES (@path, @name, @opened)
            ON CONFLICT(path) DO UPDATE SET name = @name, last_opened_utc = @opened;
            """,
            new { path, name, opened = Timestamps.Format(DateTimeOffset.UtcNow) },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM recent_repositories
            WHERE path NOT IN (
                SELECT path FROM recent_repositories
                ORDER BY last_opened_utc DESC
                LIMIT @keep
            );
            """,
            new { keep = MaxEntries },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ForgetAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM recent_repositories WHERE path = @path;",
            new { path },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed record RecentRepositoryRow(string Path, string Name, string LastOpenedUtc);
}

/// <summary>
/// SQLite has no date type, so timestamps are stored as round-trip strings. The "O" format is
/// ordinally sortable, which is what lets <c>ORDER BY</c> on the text column be chronological.
/// </summary>
internal static class Timestamps
{
    public static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
