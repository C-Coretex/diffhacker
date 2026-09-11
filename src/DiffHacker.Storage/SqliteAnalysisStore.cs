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
    /// <inheritdoc cref="AnalysisLibraryPolicy.RetentionLimit"/>
    private const int HistoryLimit = AnalysisLibraryPolicy.RetentionLimit;

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
               reviewed_json    AS ReviewedJson,
               grouping_mode    AS GroupingMode,
               options_json     AS OptionsJson,
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
                 document_json, statistics_json, diagnostics_json, files_json, reviewed_json,
                 grouping_mode, options_json, trace_json)
            VALUES (@id, @repositoryPath, @schemaVersion, @createdAtUtc, @headCommit, @providerName,
                    @model, @inputTokens, @outputTokens, @costUsd, @durationMs, @repairRounds,
                    @documentJson, @statisticsJson, @diagnosticsJson, @filesJson, @reviewedJson,
                    @groupingMode, @optionsJson, @traceJson);
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

                // Null rather than "[]" for a fresh run, so a column that was never written looks
                // the same as one written empty and neither reads as a claim about anything.
                reviewedJson = analysis.ReviewedNodeIds.Count == 0
                    ? null
                    : JsonSerializer.Serialize(analysis.ReviewedNodeIds, StorageJson.Options),

                // Null on a fresh run for the same reason: a run has no opinion about which grouping
                // to open in, and the application-wide default is not this analysis's business.
                groupingMode = analysis.Grouping is { } grouping
                    ? AnalysisGroupingNames.Of(grouping)
                    : null,
                optionsJson = analysis.Requested is { } requested
                    ? JsonSerializer.Serialize(requested, StorageJson.Options)
                    : null,
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

    public async ValueTask<IReadOnlyList<AnalysisSummary>> ListSummariesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The numbers come out of the JSON columns in SQL rather than by deserialising them, and
        // document_json is never selected: a library of twenty runs should cost twenty small rows,
        // not twenty five-hundred-node graphs parsed to count them. The paths are the camelCase
        // spellings StorageJson writes.
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            """
            SELECT id                                                        AS Id,
                   repository_path                                           AS RepositoryPath,
                   created_at_utc                                            AS CreatedAtUtc,
                   head_commit                                               AS HeadCommit,
                   provider_name                                             AS ProviderName,
                   model                                                     AS Model,
                   input_tokens                                              AS InputTokens,
                   output_tokens                                             AS OutputTokens,
                   cost_usd                                                  AS CostUsd,
                   duration_ms                                               AS DurationMs,
                   json_extract(statistics_json, '$.changeset.totalFiles')        AS FileCount,
                   json_extract(statistics_json, '$.changeset.totalLinesAdded')   AS LinesAdded,
                   json_extract(statistics_json, '$.changeset.totalLinesRemoved') AS LinesRemoved,
                   json_extract(statistics_json, '$.nodeCount')              AS NodeCount,
                   json_extract(statistics_json, '$.containerCount')         AS ContainerCount,
                   json_array_length(trace_json, '$.toolCalls')              AS ToolCallCount
              FROM analyses
             WHERE repository_path = @repositoryPath
             ORDER BY created_at_utc DESC;
            """,
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(static row => row.ToSummary())];
    }

    public async ValueTask<bool> DeleteOneAsync(string analysisId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // The reviewed marks and the chosen grouping are columns on the same row, so they go with it
        // and nothing is left behind to prune.
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM analyses WHERE id = @analysisId;",
            new { analysisId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return deleted > 0;
    }

    public async ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM analyses WHERE repository_path = @repositoryPath;",
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> SetNodesReviewedAsync(
        string analysisId,
        IReadOnlyList<string> nodeIds,
        bool reviewed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Read, modify and write inside one transaction: two windows toggling different nodes of the
        // same analysis would otherwise each write a set that never saw the other's change.
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var stored = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT reviewed_json FROM analyses WHERE id = @analysisId;",
            new { analysisId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var marks = new SortedSet<string>(Read(stored), StringComparer.Ordinal);

        foreach (var nodeId in nodeIds)
        {
            if (reviewed)
            {
                marks.Add(nodeId);
            }
            else
            {
                marks.Remove(nodeId);
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE analyses SET reviewed_json = @reviewedJson WHERE id = @analysisId;",
            new
            {
                analysisId,
                reviewedJson = marks.Count == 0
                    ? null
                    : JsonSerializer.Serialize(marks, StorageJson.Options),
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Sorted, so two callers that marked the same nodes in different orders are handed the same
        // answer and the renderer's own ordering never depends on click order.
        return [.. marks];
    }

    public async ValueTask SetGroupingAsync(
        string analysisId,
        AnalysisGrouping grouping,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // One column, one row, no read first: unlike the reviewed marks there is nothing to merge —
        // the last reviewer to choose a grouping is looking at it now.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE analyses SET grouping_mode = @groupingMode WHERE id = @analysisId;",
            new { analysisId, groupingMode = AnalysisGroupingNames.Of(grouping) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// The stored marks, tolerating both a column never written and one holding something this build
    /// cannot read. A malformed set is worth losing quietly; it is a record of what someone clicked,
    /// and refusing to open the analysis over it would be a far worse trade.
    /// </summary>
    private static List<string> Read(string? reviewedJson)
    {
        if (string.IsNullOrWhiteSpace(reviewedJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(reviewedJson, StorageJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Both halves of a run's trace in one document, so the row has one JSON column.</summary>
    private sealed record AnalysisTrace(
        IReadOnlyList<LlmToolCallRecord> ToolCalls,
        IReadOnlyList<string> ProgressMessages);

    private sealed record SummaryRow
    {
        public required string Id { get; init; }

        public required string RepositoryPath { get; init; }

        public required string CreatedAtUtc { get; init; }

        public string? HeadCommit { get; init; }

        public required string ProviderName { get; init; }

        public required string Model { get; init; }

        public long InputTokens { get; init; }

        public long OutputTokens { get; init; }

        public string? CostUsd { get; init; }

        public long DurationMs { get; init; }

        // Nullable because json_extract answers NULL for a path the stored JSON does not have, and a
        // row from an older build is still a row worth listing.
        public long? FileCount { get; init; }

        public long? LinesAdded { get; init; }

        public long? LinesRemoved { get; init; }

        public long? NodeCount { get; init; }

        public long? ContainerCount { get; init; }

        public long? ToolCallCount { get; init; }

        public AnalysisSummary ToSummary() => new()
        {
            Id = Id,
            RepositoryPath = RepositoryPath,
            CreatedAtUtc = Timestamps.Parse(CreatedAtUtc),
            HeadCommit = HeadCommit,
            ProviderDisplayName = ProviderName,
            Model = Model,
            Usage = ReadUsage(InputTokens, OutputTokens, CostUsd),
            Duration = TimeSpan.FromMilliseconds(DurationMs),
            FileCount = (int)(FileCount ?? 0),
            LinesAdded = (int)(LinesAdded ?? 0),
            LinesRemoved = (int)(LinesRemoved ?? 0),
            NodeCount = (int)(NodeCount ?? 0),
            ContainerCount = (int)(ContainerCount ?? 0),
            ToolCallCount = (int)(ToolCallCount ?? 0),
        };
    }

    /// <summary>
    /// Usage as stored, shared by the full row and the summary so the two cannot disagree about what
    /// an absent cost means: unknown, never zero.
    /// </summary>
    private static LlmUsage ReadUsage(long inputTokens, long outputTokens, string? costUsd) => new()
    {
        InputTokens = inputTokens,
        OutputTokens = outputTokens,
        IsReported = inputTokens > 0 || outputTokens > 0,
        EstimatedCostUsd = decimal.TryParse(
            costUsd, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost)
            ? cost
            : null,
    };

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

        /// <summary>Null before schema 6, and null again whenever nothing is marked.</summary>
        public string? ReviewedJson { get; init; }

        /// <summary>Null before schema 7, and null until the reviewer chooses a grouping.</summary>
        public string? GroupingMode { get; init; }

        /// <summary>Null before schema 9, where what the run asked for is inferred from the document.</summary>
        public string? OptionsJson { get; init; }

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
                Usage = ReadUsage(InputTokens, OutputTokens, CostUsd),
                Duration = TimeSpan.FromMilliseconds(DurationMs),
                RepairRounds = RepairRounds,
                Document = LegacyAnalysisDocument.Read(DocumentJson),
                Statistics = JsonSerializer.Deserialize<AnalysisStatistics>(StatisticsJson, StorageJson.Options)!,
                Diagnostics =
                    JsonSerializer.Deserialize<List<AnalysisDiagnostic>>(DiagnosticsJson, StorageJson.Options) ?? [],
                ChangedFiles = FilesJson is null
                    ? []
                    : JsonSerializer.Deserialize<List<ChangedFileFacts>>(FilesJson, StorageJson.Options) ?? [],
                ReviewedNodeIds = Read(ReviewedJson),
                Grouping = AnalysisGroupingNames.Parse(GroupingMode),
                Requested = OptionsJson is null
                    ? null
                    : JsonSerializer.Deserialize<AnalysisRunOptions>(OptionsJson, StorageJson.Options),
                ToolCalls = trace?.ToolCalls ?? [],
                ProgressMessages = trace?.ProgressMessages ?? [],
            };
        }
    }
}
