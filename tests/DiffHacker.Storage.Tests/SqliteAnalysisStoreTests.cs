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
        stored.Document.DependencyContainers[0].EntryNodeId.ShouldBe("src/Contract.cs");
        stored.Document.ClusterContainers.Count.ShouldBe(2);
        stored.Document.Edges[0].Kind.ShouldBe(AnalysisEdgeKind.Conceptual);

        stored.Statistics.NodeCount.ShouldBe(analysis.Statistics.NodeCount);
        stored.Statistics.Changeset.Languages.ShouldBe(["C#"]);
        stored.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe(AnalysisDiagnosticCodes.Cycle);
        stored.ToolCalls.ShouldHaveSingleItem().ToolName.ShouldBe("get_file_diff");
        stored.ProgressMessages.ShouldBe(["Reading the contract"]);
    }

    [Fact]
    public async Task Implementation_groups_keep_the_difference_between_none_and_not_asked()
    {
        var asked = Sample() with
        {
            Id = "asked",
            Document = Sample().Document with
            {
                ImplementationGroups =
                [
                    new AnalysisImplementationGroup
                    {
                        AbstractionNodeId = "src/Contract.cs",
                        ImplementationNodeIds = ["src/Caller.cs"],
                    },
                ],
            },
        };

        await _store.SaveAsync(asked, TestContext.Current.CancellationToken);

        var group = (await _store.FindAsync("asked", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Document.ImplementationGroups.ShouldNotBeNull().ShouldHaveSingleItem();

        group.AbstractionNodeId.ShouldBe("src/Contract.cs");
        group.ImplementationNodeIds.ShouldBe(["src/Caller.cs"]);

        var unasked = Sample() with { Id = "unasked", Document = Sample().Document with { ImplementationGroups = null } };

        await _store.SaveAsync(unasked, TestContext.Current.CancellationToken);

        (await _store.FindAsync("unasked", TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Document.ImplementationGroups.ShouldBeNull();
    }

    [Fact]
    public async Task The_changeset_the_run_was_made_from_is_stored_with_it()
    {
        // An analysis is a photograph of one changeset, and these are part of the photograph. Read
        // back from the working tree instead, they would show today's numbers on a diagram of a
        // change that has since moved on.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        var stored = (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        stored.ChangedFiles.Select(static file => file.Path)
            .ShouldBe(["src/Contract.cs", "assets/icon.png"]);

        var contract = stored.ChangedFiles[0];
        contract.Status.ShouldBe(ChangeStatus.Modified);
        contract.LinesAdded.ShouldBe(12);
        contract.LinesRemoved.ShouldBe(3);
        contract.Language.ShouldBe("C#");
        contract.Project.ShouldBe("DiffHacker");

        // Absent, not zero, on the way back out too.
        var icon = stored.ChangedFiles[1];
        icon.IsBinary.ShouldBeTrue();
        icon.LinesAdded.ShouldBeNull();
        icon.LinesRemoved.ShouldBeNull();
        icon.Language.ShouldBeNull();
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
        json.ShouldContain("\"changed\"");
        json.ShouldNotContain("UnchangedRelevant");

        // And entry_point is not in the document at all any more: it belongs to a container of one
        // grouping, so the host derives it for whichever grouping it projects.
        json.ShouldNotContain("entry_point");
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

    [Fact]
    public async Task Marking_a_node_reviewed_survives_a_restart()
    {
        // Iteration 10 requirement 7: what makes a three-hundred-node review survivable is that
        // closing the application does not throw it away.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetNodesReviewedAsync(
            "analysis1",
            ["src/Contract.cs", "src/Caller.cs"],
            reviewed: true,
            TestContext.Current.CancellationToken);

        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteAnalysisStore(_database);

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .ReviewedNodeIds.ShouldBe(["src/Caller.cs", "src/Contract.cs"]);
    }

    [Fact]
    public async Task Unmarking_removes_only_what_it_was_asked_to_remove()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetNodesReviewedAsync(
            "analysis1",
            ["src/Contract.cs", "src/Caller.cs"],
            reviewed: true,
            TestContext.Current.CancellationToken);

        var remaining = await _store.SetNodesReviewedAsync(
            "analysis1",
            ["src/Contract.cs"],
            reviewed: false,
            TestContext.Current.CancellationToken);

        // The answer is every id still marked, not the ones this call touched — which is what lets
        // the interface replace its state from one response instead of reconciling a delta.
        remaining.ShouldBe(["src/Caller.cs"]);
    }

    [Fact]
    public async Task Marking_the_same_node_twice_does_not_record_it_twice()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetNodesReviewedAsync(
            "analysis1", ["src/Caller.cs"], reviewed: true, TestContext.Current.CancellationToken);

        var marks = await _store.SetNodesReviewedAsync(
            "analysis1", ["src/Caller.cs"], reviewed: true, TestContext.Current.CancellationToken);

        marks.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Marks_belong_to_one_analysis_and_do_not_leak_into_the_next_run()
    {
        // The consequence of storing marks on the analysis row, stated as a test rather than left to
        // be discovered: a re-run is a new row and starts with nothing marked. Iteration 10 reports
        // this deliberately.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetNodesReviewedAsync(
            "analysis1", ["src/Caller.cs"], reviewed: true, TestContext.Current.CancellationToken);

        await _store.SaveAsync(
            Sample() with { Id = "analysis2", CreatedAtUtc = DateTimeOffset.UnixEpoch.AddDays(1) },
            TestContext.Current.CancellationToken);

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .ReviewedNodeIds.ShouldBeEmpty();

        // And the older run kept its own, so nothing was overwritten on the way.
        (await _store.FindAsync("analysis1", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .ReviewedNodeIds.ShouldBe(["src/Caller.cs"]);
    }

    [Fact]
    public async Task A_fresh_analysis_has_chosen_no_grouping_and_the_default_applies()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Grouping.ShouldBeNull();
    }

    [Fact]
    public async Task The_grouping_a_reviewer_chose_survives_a_restart()
    {
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetGroupingAsync(
            "analysis1", AnalysisGrouping.ChangeClusters, TestContext.Current.CancellationToken);

        await Restart();

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Grouping.ShouldBe(AnalysisGrouping.ChangeClusters);
    }

    [Fact]
    public async Task Choosing_a_grouping_leaves_the_model_s_document_untouched()
    {
        // The invariant IAnalysisStore documents: the two methods that change a stored analysis write
        // the reviewer's own state, and neither can edit the answer.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        var before = await Document();

        await _store.SetGroupingAsync(
            "analysis1", AnalysisGrouping.ChangeClusters, TestContext.Current.CancellationToken);

        (await Document()).ShouldBe(before);
    }

    [Fact]
    public async Task A_grouping_belongs_to_one_analysis_and_a_re_run_starts_at_the_default()
    {
        // The same trade the reviewed marks make, for the same reason: the choice lives on the row,
        // and a re-run is a new row.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetGroupingAsync(
            "analysis1", AnalysisGrouping.ChangeClusters, TestContext.Current.CancellationToken);

        await _store.SaveAsync(
            Sample() with { Id = "analysis2", CreatedAtUtc = DateTimeOffset.UnixEpoch.AddDays(1) },
            TestContext.Current.CancellationToken);

        (await _store.GetLatestAsync("/repo", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Grouping.ShouldBeNull();

        (await _store.FindAsync("analysis1", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Grouping.ShouldBe(AnalysisGrouping.ChangeClusters);
    }

    [Fact]
    public async Task The_grouping_column_holds_the_spelling_the_schema_uses()
    {
        // It crosses the JSON-RPC bridge as this string, so a C# name in the column would be a
        // second spelling of the same thing and one of them would eventually be wrong.
        await _store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

        await _store.SetGroupingAsync(
            "analysis1", AnalysisGrouping.ChangeClusters, TestContext.Current.CancellationToken);

        await using var connection = await _database.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT grouping_mode FROM analyses WHERE id = 'analysis1';";

        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))
            .ShouldBe("change_clusters");
    }

    /// <summary>Closes the database and opens it again, the way the application does on restart.</summary>
    private async Task Restart()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(_directory.DatabaseFile, NullLogger<AppDatabase>.Instance);
        _store = new SqliteAnalysisStore(_database);
    }

    private async Task<string> Document()
    {
        await using var connection = await _database.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_json FROM analyses WHERE id = 'analysis1';";

        return (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string)
            .ShouldNotBeNull();
    }

    internal static Analysis Sample()
    {
        var document = new AnalysisResult
        {
            Summary = "The contract grew a field.",
            OverallRisks = ["Nothing was added to the tests."],
            DependencyReadingOrder = ["src/Contract.cs", "src/Caller.cs"],
            DependencyContainers =
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

            // The same two nodes grouped by theme, so the round trip carries both groupings and a
            // test can read one back without the other having been invented on the way.
            ClusterReadingOrder = ["src/Caller.cs", "src/Contract.cs"],
            ClusterContainers =
            [
                new AnalysisContainer
                {
                    Id = "call-sites",
                    Title = "Call sites",
                    Summary = "Where the contract is constructed.",
                    Explanation = "Grouped by concern rather than by the path through them.",
                    DisplayOrder = 1,
                    EntryNodeId = "src/Caller.cs",
                    NodeIds = ["src/Caller.cs"],
                },
                new AnalysisContainer
                {
                    Id = "contracts",
                    Title = "Contracts",
                    Summary = "The shape everything agrees on.",
                    Explanation = "Its own concern in this grouping.",
                    DisplayOrder = 2,
                    EntryNodeId = "src/Contract.cs",
                    NodeIds = ["src/Contract.cs"],
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
                    States = [AnalysisNodeState.Changed],
                },
                new AnalysisNode
                {
                    Id = "src/Caller.cs",
                    FilePath = "src/Caller.cs",
                    Title = "The caller",
                    WhatChanged = "Nothing, but it has to be read.",
                    WhyItChanged = "It constructs the contract.",
                    Importance = 2,
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
            new ChangedFile
            {
                // No line counts: the case that proves absent survives the round trip as absent
                // rather than coming back as zero.
                Path = "assets/icon.png",
                Status = ChangeStatus.Deleted,
                IsBinary = true,
                Project = new ProjectReference("assets", "assets", null),
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
                AnalysisGrouping.DependencyFlow,
                ChangesetStatistics.From(changed),
                AnalysisGraph.Build(document.Nodes, document.Edges)),
            ChangedFiles = [.. changed.Select(ChangedFileFacts.From)],
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
