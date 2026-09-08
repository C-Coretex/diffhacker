using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Llm;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Storage.Tests;

public sealed class SqliteAnalysisStoreTests : IAsyncLifetime
{
    private readonly TemporaryDataDirectory _directory = new();
    private AppDatabase _database = null!;
    private SqliteAnalysisStore _store = null!;

    public ValueTask InitializeAsync()
    {
        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteAnalysisStore(_database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public async Task A_repository_with_no_analysis_reports_none_rather_than_an_empty_one()
    {
        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken)).ShouldBeNull();
        (await _store.ListAsync("/repo", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_analysis_survives_the_round_trip_whole()
    {
        var analysis = Sample();

        await _store.SaveAsync(analysis, TestContext.Current.CancellationToken);

        var stored = (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        stored.Id.ShouldBe(analysis.Id);
        stored.SchemaVersion.ShouldBe(analysis.SchemaVersion);
        stored.HeadCommit.ShouldBe("head0001");
        stored.Model.ShouldBe("gpt-4o");
        stored.RepairRounds.ShouldBe(1);
        stored.Duration.ShouldBe(TimeSpan.FromSeconds(42));
        stored.Usage.InputTokens.ShouldBe(1000);
        stored.Usage.EstimatedCostUsd.ShouldBe(0.25m);

        stored.Document.Summary.ShouldBe(analysis.Document.Summary);
        stored.Document.Nodes.Count.ShouldBe(analysis.Document.Nodes.Count);
        stored.Document.Containers[0].EntryNodeId.ShouldBe("src/Contract.cs");
        stored.Document.Edges[0].Kind.ShouldBe(AnalysisEdgeKind.Conceptual);

        stored.Statistics.NodeCount.ShouldBe(analysis.Statistics.NodeCount);
        stored.Statistics.Changeset.Languages.ShouldBe(["C#"]);
        stored.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(AnalysisDiagnosticCodes.Cycle);
        stored.ToolCalls.ShouldHaveSingleItem().ToolName.ShouldBe("get_file_diff");
        stored.ProgressMessages.ShouldBe(["Reading the contract"]);
    }

    [Fact]
    public async Task Node_states_come_back_spelled_the_way_the_schema_spells_them()
    {
        // The states are an array of enums, which is the one shape the contract generator does not
        // attach a converter to on its own. Getting this wrong turns unchanged_relevant into
        // UnchangedRelevant somewhere between the model and the screen.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        var stored = (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        stored.Document.Nodes[1].States.ShouldBe([AnalysisNodeState.UnchangedRelevant]);

        await using var connection = await _database.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_json FROM analyses;";

        var json = (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string).ShouldNotBeNull();

        json.ShouldContain("\"unchanged_relevant\"");
        json.ShouldContain("\"entry_point\"");
        json.ShouldNotContain("UnchangedRelevant");
    }

    [Fact]
    public async Task A_cost_that_was_never_known_stays_unknown_rather_than_becoming_zero()
    {
        var analysis = Sample() with
        {
            Usage = new LlmUsage { InputTokens = 10, OutputTokens = 2, IsReported = true },
        };

        await _store.SaveAsync(analysis, TestContext.Current.CancellationToken);

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Usage.EstimatedCostUsd.ShouldBeNull();
    }

    [Fact]
    public async Task The_most_recent_analysis_is_the_one_returned_and_the_history_is_ordered()
    {
        for (var i = 0; i < 3; i++)
        {
            await _store.SaveAsync(
                Sample() with
                {
                    Id = $"run{i}",
                    CreatedAtUtc = DateTimeOffset.UnixEpoch.AddHours(i),
                },
                TestContext.Current.CancellationToken);
        }

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Id.ShouldBe("run2");

        (await _store.ListAsync("/repo", TestContext.Current.CancellationToken))
            .Select(static analysis => analysis.Id).ShouldBe(["run2", "run1", "run0"]);
    }

    [Fact]
    public async Task The_history_is_capped_and_the_oldest_go_first()
    {
        for (var i = 0; i < 25; i++)
        {
            await _store.SaveAsync(
                Sample() with { Id = $"run{i:D2}", CreatedAtUtc = DateTimeOffset.UnixEpoch.AddHours(i) },
                TestContext.Current.CancellationToken);
        }

        var stored = await _store.ListAsync("/repo", TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(20);
        stored[^1].Id.ShouldBe("run05");
    }

    [Fact]
    public async Task Repositories_do_not_see_each_others_analyses()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
        await _store.SaveAsync(
            Sample() with { Id = "other", RepositoryPath = "/elsewhere" },
            TestContext.Current.CancellationToken);

        (await _store.ListAsync("/repo", TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        (await _store.GetLatestAsync("/elsewhere", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Id.ShouldBe("other");
    }

    [Fact]
    public async Task An_analysis_can_be_found_by_its_own_id()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        (await _store.FindAsync("analysis1", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().RepositoryPath.ShouldBe("/repo");

        (await _store.FindAsync("nothing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_forgets_one_repository_and_leaves_the_others()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
        await _store.SaveAsync(
            Sample() with { Id = "other", RepositoryPath = "/elsewhere" },
            TestContext.Current.CancellationToken);

        await _store.DeleteAsync("/repo", TestContext.Current.CancellationToken);

        (await _store.ListAsync("/repo", TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await _store.ListAsync("/elsewhere", TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_stored_analysis_reopens_after_a_restart()
    {
        // Requirement 5: reopening never re-runs the LLM, which is only true if the whole result
        // came back off disk rather than out of a process that is no longer running.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteAnalysisStore(_database);

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Document.Nodes.Count.ShouldBe(2);
    }

    internal static Analysis Sample()
    {
        var document = new AnalysisResult
        {
            Summary = "The contract grew a field.",
            OverallRisks = ["Nothing was added to the tests."],
            ReadingOrder = ["src/Contract.cs", "src/Caller.cs"],
            Containers =
            [
                new AnalysisContainer
                {
                    Id = "core",
                    Title = "The contract",
                    Summary = "A field arrived.",
                    Explanation = "And its caller followed.",
                    DisplayOrder = 1,
                    EntryNodeId = "src/Contract.cs",
                    NodeIds = ["src/Contract.cs", "src/Caller.cs"],
                },
            ],
            Nodes =
            [
                new AnalysisNode
                {
                    Id = "src/Contract.cs",
                    FilePath = "src/Contract.cs",
                    Title = "The contract",
                    WhatChanged = "A tenant field.",
                    WhyItChanged = "Callers need to pass one.",
                    Importance = 5,
                    Rank = 1,
                    States = [AnalysisNodeState.Changed, AnalysisNodeState.EntryPoint],
                },
                new AnalysisNode
                {
                    Id = "src/Caller.cs",
                    FilePath = "src/Caller.cs",
                    Title = "The caller",
                    WhatChanged = "Nothing, but it has to be read.",
                    WhyItChanged = "It constructs the contract.",
                    Importance = 2,
                    Rank = 2,
                    States = [AnalysisNodeState.UnchangedRelevant],
                },
            ],
            Edges =
            [
                new AnalysisEdge
                {
                    SourceNodeId = "src/Contract.cs",
                    TargetNodeId = "src/Caller.cs",
                    Kind = AnalysisEdgeKind.Conceptual,
                    Explanation = "Read the decision before its consequence.",
                },
            ],
        };

        var changed = new[]
        {
            new ChangedFile
            {
                Path = "src/Contract.cs",
                Status = ChangeStatus.Modified,
                LinesAdded = 12,
                LinesRemoved = 3,
                IsBinary = false,
                Language = "C#",
                Project = new ProjectReference("DiffHacker", "src", "DiffHacker.csproj"),
            },
        };

        return new Analysis
        {
            Id = "analysis1",
            RepositoryPath = "/repo",
            SchemaVersion = "1.7.0",
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            HeadCommit = "head0001",
            ProviderDisplayName = "Test",
            Model = "gpt-4o",
            Usage = new LlmUsage
            {
                InputTokens = 1000,
                OutputTokens = 200,
                IsReported = true,
                EstimatedCostUsd = 0.25m,
            },
            Duration = TimeSpan.FromSeconds(42),
            RepairRounds = 1,
            Document = document,
            Statistics = AnalysisStatistics.From(
                document,
                ChangesetStatistics.From(changed),
                AnalysisGraph.Build(document.Nodes, document.Edges)),
            Diagnostics =
            [
                AnalysisDiagnostic.Warning(
                    AnalysisDiagnosticCodes.Cycle,
                    "src/Caller.cs",
                    "These nodes form a cycle in the reading flow: src/Caller.cs, src/Contract.cs."),
            ],
            ToolCalls =
            [
                new LlmToolCallRecord
                {
                    Ordinal = 1,
                    Turn = 1,
                    ToolName = "get_file_diff",
                    ArgumentsPreview = """{"path":"src/Contract.cs"}""",
                    ResultBytes = 900,
                    Duration = TimeSpan.FromMilliseconds(11),
                },
            ],
            ProgressMessages = ["Reading the contract"],
        };
    }
}
