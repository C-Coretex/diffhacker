using System.Globalization;
using System.Text.Json;
using Dapper;
using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Llm;
using DiffHacker.Core.Settings;

namespace DiffHacker.Storage;

/// <summary>
/// SQLite-backed project profiles, keyed by worktree root.
/// <para>
/// Three writes, and none of them can reach another's columns. That is the mechanism behind
/// "manual edits survive regeneration": <see cref="SaveGeneratedAsync"/> names the generated
/// columns, <see cref="SaveUserSectionsAsync"/> names the user's, and neither statement mentions
/// the other's. A caller cannot get it wrong by passing the wrong object.
/// </para>
/// </summary>
public sealed class SqliteProjectProfileStore(AppDatabase database, TimeProvider clock) : IProjectProfileStore
{
    /// <summary>
    /// How many finished runs to keep per repository. Enough to answer "what did the last few
    /// attempts do?", few enough that a database nobody prunes stays small.
    /// </summary>
    private const int RunHistoryLimit = 10;

    private const string SelectColumns =
        """
        SELECT repository_path       AS RepositoryPath,
               document_json         AS DocumentJson,
               user_notes            AS UserNotes,
               custom_instructions   AS CustomInstructions,
               excluded_globs        AS ExcludedGlobs,
               character_budget      AS CharacterBudget,
               document_characters   AS DocumentCharacters,
               generated_at_utc      AS GeneratedAtUtc,
               generated_from_commit AS GeneratedFromCommit,
               generated_by_provider AS GeneratedByProvider,
               generated_by_model    AS GeneratedByModel,
               created_at_utc        AS CreatedAtUtc,
               updated_at_utc        AS UpdatedAtUtc
        FROM project_profiles
        """;

    public async ValueTask<ProjectProfile?> GetAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<ProjectProfileRow>(new CommandDefinition(
            SelectColumns + " WHERE repository_path = @repositoryPath;",
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row?.ToProfile();
    }

    public async ValueTask SaveGeneratedAsync(
        string repositoryPath,
        ProjectProfileDocument document,
        ProfileProvenance provenance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(provenance);

        var now = Timestamps.Format(clock.GetUtcNow());

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_profiles
                (repository_path, document_json, user_notes, custom_instructions,
                 document_characters, generated_at_utc, generated_from_commit,
                 generated_by_provider, generated_by_model, created_at_utc, updated_at_utc)
            VALUES (@repositoryPath, @documentJson, '', '',
                    @documentCharacters, @generatedAtUtc, @generatedFromCommit,
                    @generatedByProvider, @generatedByModel, @now, @now)
            ON CONFLICT(repository_path) DO UPDATE SET
                document_json         = @documentJson,
                document_characters   = @documentCharacters,
                generated_at_utc      = @generatedAtUtc,
                generated_from_commit = @generatedFromCommit,
                generated_by_provider = @generatedByProvider,
                generated_by_model    = @generatedByModel,
                updated_at_utc        = @now;
            """,
            new
            {
                repositoryPath,
                documentJson = JsonSerializer.Serialize(document, StorageJson.Options),
                documentCharacters = provenance.DocumentCharacters,
                generatedAtUtc = Timestamps.Format(provenance.GeneratedAtUtc),
                generatedFromCommit = provenance.CommitSha,
                generatedByProvider = provenance.ProviderDisplayName,
                generatedByModel = provenance.Model,
                now,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask SaveEditedDocumentAsync(
        string repositoryPath,
        ProjectProfileDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Provenance is left alone. The document still came from that run and that commit; the
        // user has edited it since, which is a different fact and not one this row records.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE project_profiles
               SET document_json       = @documentJson,
                   document_characters = @documentCharacters,
                   updated_at_utc      = @now
             WHERE repository_path = @repositoryPath;
            """,
            new
            {
                repositoryPath,
                documentJson = JsonSerializer.Serialize(document, StorageJson.Options),
                documentCharacters = ProfileBudget.Measure(document),
                now = Timestamps.Format(clock.GetUtcNow()),
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask SaveUserSectionsAsync(
        string repositoryPath,
        string userNotes,
        string customInstructions,
        IReadOnlyList<string> customExcludedGlobs,
        int? characterBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customExcludedGlobs);

        var now = Timestamps.Format(clock.GetUtcNow());
        var globs = SensitiveFiles.NormaliseCustom(customExcludedGlobs);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        // An insert as well as an update: custom instructions are useful before a profile has ever
        // been generated, and "ignore the generated/ folder" is a thing to write down on day one.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_profiles
                (repository_path, user_notes, custom_instructions, excluded_globs,
                 character_budget, created_at_utc, updated_at_utc)
            VALUES (@repositoryPath, @userNotes, @customInstructions, @excludedGlobs,
                    @characterBudget, @now, @now)
            ON CONFLICT(repository_path) DO UPDATE SET
                user_notes          = @userNotes,
                custom_instructions = @customInstructions,
                excluded_globs      = @excludedGlobs,
                character_budget    = @characterBudget,
                updated_at_utc      = @now;
            """,
            new
            {
                repositoryPath,
                userNotes = userNotes ?? string.Empty,
                customInstructions = customInstructions ?? string.Empty,
                excludedGlobs = globs.Count == 0 ? null : JsonSerializer.Serialize(globs, StorageJson.Options),
                characterBudget,
                now,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM project_profiles WHERE repository_path = @repositoryPath;",
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async ValueTask RecordRunAsync(ProfileRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_profile_runs
                (id, repository_path, started_at_utc, finished_at_utc, outcome, failure_code,
                 provider_name, model, input_tokens, output_tokens, cost_usd, duration_ms, trace_json)
            VALUES (@id, @repositoryPath, @startedAtUtc, @finishedAtUtc, @outcome, @failureCode,
                    @providerName, @model, @inputTokens, @outputTokens, @costUsd, @durationMs, @traceJson);
            """,
            new
            {
                id = run.Id,
                repositoryPath = run.RepositoryPath,
                startedAtUtc = Timestamps.Format(run.StartedAtUtc),
                finishedAtUtc = run.FinishedAtUtc is { } finished ? Timestamps.Format(finished) : null,
                outcome = run.Outcome.ToString().ToLowerInvariant(),
                failureCode = run.FailureCode,
                providerName = run.ProviderDisplayName,
                model = run.Model,
                inputTokens = run.Usage.InputTokens,
                outputTokens = run.Usage.OutputTokens,

                // Invariant text, not REAL: the same reason provider prices are stored that way.
                // A cost with no entry in the price table stays null, never zero.
                costUsd = run.Usage.EstimatedCostUsd?.ToString(CultureInfo.InvariantCulture),
                durationMs = (long)run.Duration.TotalMilliseconds,
                traceJson = JsonSerializer.Serialize(
                    new RunTrace(run.ToolCalls, run.ProgressMessages),
                    StorageJson.Options),
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Pruned on write rather than on a timer: the history is only ever added to here, so this
        // is the one place it can grow, and it is where it is cheapest to trim.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM project_profile_runs
             WHERE repository_path = @repositoryPath
               AND id NOT IN (
                   SELECT id FROM project_profile_runs
                    WHERE repository_path = @repositoryPath
                    ORDER BY started_at_utc DESC
                    LIMIT @keep);
            """,
            new { repositoryPath = run.RepositoryPath, keep = RunHistoryLimit },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<ProfileRun>> ListRunsAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ProfileRunRow>(new CommandDefinition(
            """
            SELECT id              AS Id,
                   repository_path AS RepositoryPath,
                   started_at_utc  AS StartedAtUtc,
                   finished_at_utc AS FinishedAtUtc,
                   outcome         AS Outcome,
                   failure_code    AS FailureCode,
                   provider_name   AS ProviderName,
                   model           AS Model,
                   input_tokens    AS InputTokens,
                   output_tokens   AS OutputTokens,
                   cost_usd        AS CostUsd,
                   duration_ms     AS DurationMs,
                   trace_json      AS TraceJson
              FROM project_profile_runs
             WHERE repository_path = @repositoryPath
             ORDER BY started_at_utc DESC;
            """,
            new { repositoryPath },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(static row => row.ToRun())];
    }

    /// <summary>Both halves of a run's trace in one document, so the row has one JSON column.</summary>
    private sealed record RunTrace(
        IReadOnlyList<LlmToolCallRecord> ToolCalls,
        IReadOnlyList<string> ProgressMessages);

    private sealed record ProjectProfileRow
    {
        public required string RepositoryPath { get; init; }

        public string? DocumentJson { get; init; }

        public required string UserNotes { get; init; }

        public required string CustomInstructions { get; init; }

        public string? ExcludedGlobs { get; init; }

        public int? CharacterBudget { get; init; }

        public int? DocumentCharacters { get; init; }

        public string? GeneratedAtUtc { get; init; }

        public string? GeneratedFromCommit { get; init; }

        public string? GeneratedByProvider { get; init; }

        public string? GeneratedByModel { get; init; }

        public required string CreatedAtUtc { get; init; }

        public required string UpdatedAtUtc { get; init; }

        public ProjectProfile ToProfile()
        {
            var document = DocumentJson is null
                ? null
                : JsonSerializer.Deserialize<ProjectProfileDocument>(DocumentJson, StorageJson.Options);

            // Provenance exists only alongside a document, and only when the run that produced it
            // recorded when it ran. Anything less is a half-fact and is reported as none.
            var provenance = document is not null && GeneratedAtUtc is { } generatedAt
                ? new ProfileProvenance
                {
                    CommitSha = GeneratedFromCommit,
                    GeneratedAtUtc = Timestamps.Parse(generatedAt),
                    ProviderDisplayName = GeneratedByProvider ?? string.Empty,
                    Model = GeneratedByModel ?? string.Empty,
                    DocumentCharacters = DocumentCharacters ?? 0,
                }
                : null;

            return new ProjectProfile
            {
                RepositoryPath = RepositoryPath,
                Document = document,
                Provenance = provenance,
                UserNotes = UserNotes,
                CustomInstructions = CustomInstructions,
                CustomExcludedGlobs = ExcludedGlobs is null
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(ExcludedGlobs, StorageJson.Options) ?? [],
                CharacterBudget = CharacterBudget,
                CreatedAtUtc = Timestamps.Parse(CreatedAtUtc),
                UpdatedAtUtc = Timestamps.Parse(UpdatedAtUtc),
            };
        }
    }

    private sealed record ProfileRunRow
    {
        public required string Id { get; init; }

        public required string RepositoryPath { get; init; }

        public required string StartedAtUtc { get; init; }

        public string? FinishedAtUtc { get; init; }

        public required string Outcome { get; init; }

        public string? FailureCode { get; init; }

        public required string ProviderName { get; init; }

        public required string Model { get; init; }

        public long InputTokens { get; init; }

        public long OutputTokens { get; init; }

        public string? CostUsd { get; init; }

        public long DurationMs { get; init; }

        public required string TraceJson { get; init; }

        public ProfileRun ToRun()
        {
            var trace = JsonSerializer.Deserialize<RunTrace>(TraceJson, StorageJson.Options);

            return new ProfileRun
            {
                Id = Id,
                RepositoryPath = RepositoryPath,
                StartedAtUtc = Timestamps.Parse(StartedAtUtc),
                FinishedAtUtc = FinishedAtUtc is null ? null : Timestamps.Parse(FinishedAtUtc),
                Outcome = Enum.TryParse<ProfileRunOutcome>(Outcome, ignoreCase: true, out var parsed)
                    ? parsed
                    : ProfileRunOutcome.Failed,
                FailureCode = FailureCode,
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
                ToolCalls = trace?.ToolCalls ?? [],
                ProgressMessages = trace?.ProgressMessages ?? [],
            };
        }
    }
}
