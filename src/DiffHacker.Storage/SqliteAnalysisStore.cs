using System.Globalization;
using System.Text.Json;
using Dapper;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Settings;

namespace DiffHacker.Storage;

/// <summary>
/// Completed analyses, as versioned JSON documents with the queryable facts lifted out beside them.
/// <para>
/// The document goes in whole and comes back whole. Flattening the graph into rows would make the
/// stored result a different artifact from the one the model produced and the one the validator
/// accepted, and every future schema change would then be a migration of the user's history rather
/// than a version stamp on it.
/// </para>
/// </summary>
public sealed class SqliteAnalysisStore(AppDatabase database) : IAnalysisStore
{
    /// <summary>
    /// Analyses kept per repository. Generous, because re-running one costs money and the point of
    /// storing them is not having to; bounded, because a five-hundred-node document is not small
    /// and an unbounded history of them would grow without anyone deciding it should.
    /// </summary>
    private const int HistoryLimit = 20;

    private const string SelectColumns =
        """
        SELECT id               AS Id,
               repository_path  AS RepositoryPath,
               schema_version   AS SchemaVersion,
               created_at_utc   AS CreatedAtUtc,
               head_commit      AS HeadCommit,
               provider_name    AS ProviderName,
               model            AS Model,
               input_tokens     AS InputTokens,
               output_tokens    AS OutputTokens,
               cost_usd         AS CostUsd,
               duration_ms      AS DurationMs,
               repair_rounds    AS RepairRounds,
               document_json    AS DocumentJson,
               statistics_json  AS StatisticsJson,
               diagnostics_json AS DiagnosticsJson,
               files_json       AS FilesJson,
               trace_json       AS TraceJson
          FROM analyses
        """;

    public async ValueTask SaveAsync(Analysis analysis, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO analyses
                (id, repository_path, schema_version, created_at_utc, head_commit, provider_name,
                 model, input_tokens, output_tokens, cost_usd, duration_ms, repair_rounds,
                 document_json, statistics_json, diagnostics_json, files_json, trace_json)
            VALUES (@id, @repositoryPath, @schemaVersion, @createdAtUtc, @headCommit, @providerName,
                    @model, @inputTokens, @outputTokens, @costUsd, @durationMs, @repairRounds,
                    @documentJson, @statisticsJson, @diagnosticsJson, @filesJson, @traceJson);
            """,
            new
            {
                id = analysis.Id,
                repositoryPath = analysis.RepositoryPath,
                schemaVersion = analysis.SchemaVersion,
                createdAtUtc = Timestamps.Format(analysis.CreatedAtUtc),
                headCommit = analysis.HeadCommit,
                providerName = analysis.ProviderDisplayName,
                model = analysis.Model,
                inputTokens = analysis.Usage.InputTokens,
                outputTokens = analysis.Usage.OutputTokens,

                // Invariant text, not REAL, and null rather than zero when the model is not in the
                // price table: "we do not know what this cost" is a different claim from "free".
                costUsd = analysis.Usage.EstimatedCostUsd?.ToString(CultureInfo.InvariantCulture),
                durationMs = (long)analysis.Duration.TotalMilliseconds,
                repairRounds = analysis.RepairRounds,
                documentJson = JsonSerializer.Serialize(analysis.Document, StorageJson.Options),
                statisticsJson = JsonSerializer.Serialize(analysis.Statistics, StorageJson.Options),
                diagnosticsJson = JsonSerializer.Serialize(analysis.Diagnostics, StorageJson.Options),
                filesJson = JsonSerializer.Serialize(analysis.ChangedFiles, StorageJson.Options),
                traceJson = JsonSerializer.Serialize(
                    new AnalysisTrace(analysis.ToolCalls, analysis.ProgressMessages),
                    StorageJson.Options),
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Pruned on write rather than on a timer: this is the only place the history grows, so it
        // is the one place trimming it is both correct and cheap.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM analyses
             WHERE repository_path = @repositoryPath
               AND id NOT IN (
                   SELECT id FROM analyses
                    WHERE repository_path = @repositoryPath
                    ORDER BY created_at_utc DESC
                    LIMIT @keep);
            """,
            new { repositoryPath = analysis.RepositoryPath, keep = HistoryLimit },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Analysis?> GetLatestAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<AnalysisRow>(new CommandDefinition(
            SelectColumns + """
             WHERE repository_path = @repositoryPath
             ORDER BY created_at_utc DESC
             LIMIT 1;
            """,
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToAnalysis();
    }

    public async ValueTask<Analysis?> FindAsync(string analysisId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<AnalysisRow>(new CommandDefinition(
            SelectColumns + " WHERE id = @analysisId;",
            new { analysisId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToAnalysis();
    }

    public async ValueTask<IReadOnlyList<Analysis>> ListAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<AnalysisRow>(new CommandDefinition(
            SelectColumns + """
             WHERE repository_path = @repositoryPath
             ORDER BY created_at_utc DESC;
            """,
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(static row => row.ToAnalysis())];
    }

    public async ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM analyses WHERE repository_path = @repositoryPath;",
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Both halves of a run's trace in one document, so the row has one JSON column.</summary>
    private sealed record AnalysisTrace(
        IReadOnlyList<LlmToolCallRecord> ToolCalls,
        IReadOnlyList<string> ProgressMessages);

    private sealed record AnalysisRow
    {
        public required string Id { get; init; }

        public required string RepositoryPath { get; init; }

        public required string SchemaVersion { get; init; }

        public required string CreatedAtUtc { get; init; }

        public string? HeadCommit { get; init; }

        public required string ProviderName { get; init; }

        public required string Model { get; init; }

        public long InputTokens { get; init; }

        public long OutputTokens { get; init; }

        public string? CostUsd { get; init; }

        public long DurationMs { get; init; }

        public int RepairRounds { get; init; }

        public required string DocumentJson { get; init; }

        public required string StatisticsJson { get; init; }

        public required string DiagnosticsJson { get; init; }

        /// <summary>
        /// Null on a row written before schema 5, which is why it is not <c>required</c>: an
        /// analysis from an older build opens with no per-file facts rather than not at all.
        /// </summary>
        public string? FilesJson { get; init; }

        public required string TraceJson { get; init; }

        public Analysis ToAnalysis()
        {
            var trace = JsonSerializer.Deserialize<AnalysisTrace>(TraceJson, StorageJson.Options);

            return new Analysis
            {
                Id = Id,
                RepositoryPath = RepositoryPath,
                SchemaVersion = SchemaVersion,
                CreatedAtUtc = Timestamps.Parse(CreatedAtUtc),
                HeadCommit = HeadCommit,
                ProviderDisplayName = ProviderName,
                Model = Model,
                Usage = new LlmUsage
                {
                    InputTokens = InputTokens,
                    OutputTokens = OutputTokens,
                    IsReported = InputTokens > 0 || OutputTokens > 0,
                    EstimatedCostUsd = decimal.TryParse(
                        CostUsd, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost)
                        ? cost
                        : null,
                },
                Duration = TimeSpan.FromMilliseconds(DurationMs),
                RepairRounds = RepairRounds,
                Document = JsonSerializer.Deserialize<AnalysisResult>(DocumentJson, StorageJson.Options)!,
                Statistics = JsonSerializer.Deserialize<AnalysisStatistics>(StatisticsJson, StorageJson.Options)!,
                Diagnostics =
                    JsonSerializer.Deserialize<List<AnalysisDiagnostic>>(DiagnosticsJson, StorageJson.Options) ?? [],
                ChangedFiles = FilesJson is null
                    ? []
                    : JsonSerializer.Deserialize<List<ChangedFileFacts>>(FilesJson, StorageJson.Options) ?? [],
                ToolCalls = trace?.ToolCalls ?? [],
                ProgressMessages = trace?.ProgressMessages ?? [],
            };
        }
    }
}
