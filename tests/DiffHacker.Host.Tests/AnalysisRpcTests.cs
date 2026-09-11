using DiffHacker.Contracts;
using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Llm;
using DiffHacker.Git;
using DiffHacker.Host.Rpc;
using DiffHacker.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The analysis RPC surface against a real database, with only the run itself faked — there is no
/// test here that reaches a provider.
/// <para>
/// The assertion that matters most is the last one: reopening a stored analysis must not start a
/// conversation. It is the whole reason the result is persisted, and the only way to prove it is to
/// count the runs the stub was asked for.
/// </para>
/// </summary>
public sealed class AnalysisRpcTests : IAsyncLifetime
{
    private readonly DirectoryInfo _dataDirectory =
        Directory.CreateTempSubdirectory("diffhacker-analysis-rpc-");

    private AppDatabase _database = null!;
    private SqliteAnalysisStore _store = null!;
    private SqliteAppSettingStore _settings = null!;
    private StubAnalysisRunner _runner = null!;
    private AnalysisRpcTarget _target = null!;

    // A real git client: the freshness check is only worth testing against a real working tree.
    private static readonly GitClient Git = CreateGit();

    public ValueTask InitializeAsync()
    {
        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        _store = new SqliteAnalysisStore(_database);
        _settings = new SqliteAppSettingStore(_database);
        _runner = new StubAnalysisRunner(_store);
        _target = Target();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        try
        {
            _dataDirectory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A file handle outliving the test is not a test failure.
        }
    }

    [Fact]
    public async Task A_repository_with_no_analysis_reports_that_rather_than_failing()
    {
        var view = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.HasAnalysis.ShouldBeFalse();
        view.RepositoryPath.ShouldBe("/repo");
        view.Nodes.ShouldBeEmpty();
        view.Containers.ShouldBeEmpty();
        view.ChangedFiles.ShouldBeEmpty();
        view.AnalysisId.ShouldBeNull();
    }

    [Fact]
    public async Task The_changeset_the_run_was_made_from_travels_with_the_graph()
    {
        // Iteration 8 requirement 2: the node boxes draw line counts, a status word and a project,
        // and none of the three is in the model's answer. They are recorded at run time and stored,
        // never re-read — joining a stored analysis against today's working tree would print
        // today's numbers on a diagram of yesterday's change.
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.ChangedFiles.Select(static file => file.Path)
            .ShouldBe(["src/Contract.cs", "src/Caller.cs", "assets/icon.png"]);

        var contract = view.ChangedFiles[0];
        contract.Status.ShouldBe(ChangedFileFactsInfoStatus.Modified);
        contract.LinesAdded.ShouldBe(12);
        contract.LinesRemoved.ShouldBe(3);
        contract.Project.ShouldBe("DiffHacker");
        contract.Language.ShouldBe("C#");
    }

    [Fact]
    public async Task A_binary_file_reaches_the_renderer_with_no_line_counts_rather_than_zero()
    {
        // "We did not count" and "we counted nothing" are different claims. A box reading +0 −0
        // makes the wrong one, so the absence has to survive every hop to the screen.
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var icon = view.ChangedFiles.Single(static file => file.Path == "assets/icon.png");

        icon.IsBinary.ShouldBeTrue();
        icon.LinesAdded.ShouldBeNull();
        icon.LinesRemoved.ShouldBeNull();
        icon.Language.ShouldBeNull();
        icon.Status.ShouldBe(ChangedFileFactsInfoStatus.Deleted);
    }

    [Fact]
    public async Task A_completed_run_comes_back_as_the_whole_view()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.HasAnalysis.ShouldBeTrue();
        view.Summary.ShouldBe("The contract grew a field.");
        view.Model.ShouldBe("gpt-4o");
        view.RepairRounds.ShouldBe(1);
        view.CostUsd.ShouldBe("0.25");
        view.Containers.ShouldHaveSingleItem().EntryNodeId.ShouldBe("src/Contract.cs");
        view.OverallRisks.ShouldHaveSingleItem();
        view.Statistics.ShouldNotBeNull().NodeCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_view_resolves_the_container_each_node_is_in_and_orders_them_for_reading()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.Nodes.Select(static node => node.Id).ShouldBe(["src/Contract.cs", "src/Caller.cs"]);
        view.Nodes.ShouldAllBe(node => node.ContainerId == "core");
        view.Nodes[0].Rank.ShouldBe(1);
        view.Nodes[0].States.ShouldContain(AnalysisNodeInfoState.Entry_point);
        view.Nodes[1].States.ShouldContain(AnalysisNodeInfoState.Unchanged_relevant);
    }

    [Fact]
    public async Task An_edge_inside_one_container_is_not_marked_as_crossing_one()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var edge = view.Edges.ShouldHaveSingleItem();

        edge.CrossesContainers.ShouldBeFalse();
        edge.Kind.ShouldBe(AnalysisEdgeInfoKind.Conceptual);
    }

    [Fact]
    public async Task Warnings_reach_the_renderer_so_a_cycle_is_visible_rather_than_silent()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var diagnostic = view.Diagnostics.ShouldHaveSingleItem();

        diagnostic.Severity.ShouldBe(AnalysisDiagnosticInfoSeverity.Warning);
        diagnostic.Code.ShouldBe(AnalysisDiagnosticCodes.Cycle);
        diagnostic.Subject.ShouldBe("src/Caller.cs");
    }

    [Fact]
    public async Task A_validation_failure_carries_the_specific_diagnostics_into_the_error()
    {
        // Requirement 4: the run fails loudly and the user is shown what was wrong. A code with no
        // detail would be the quiet failure that requirement exists to forbid.
        _runner.FailWith = AnalysisFailures.ValidationFailed;

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();

        data.Code.ShouldBe(AnalysisFailures.ValidationFailed);
        data.Args.ShouldNotBeNull()["detail"].ShouldContain("No node covers the changed file 'src/Dropped.cs'.");
    }

    [Fact]
    public async Task A_missing_provider_comes_back_as_its_own_code()
    {
        _runner.FailWith = AnalysisFailures.NoProvider;

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe(AnalysisFailures.NoProvider);
    }

    [Fact]
    public async Task A_provider_that_is_not_configured_properly_surfaces_its_configuration_code()
    {
        _runner.Throw = new LlmConfigurationException("provider_key_missing", "No key is stored.");

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe("provider_key_missing");
    }

    [Fact]
    public async Task A_failed_run_stores_nothing_that_a_later_get_could_mistake_for_a_result()
    {
        _runner.FailWith = AnalysisFailures.ValidationFailed;

        await Should.ThrowAsync<LocalRpcException>(
            async () => await _target.RunAsync(Request(), TestContext.Current.CancellationToken));

        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken)).HasAnalysis.ShouldBeFalse();
    }

    [Fact]
    public async Task Reopening_a_stored_analysis_never_runs_the_model_again()
    {
        // Verification step 7, asserted from the only log that could prove it: how many times the
        // runner was asked to run at all.
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        _runner.Runs.ShouldBe(1);

        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        _store = new SqliteAnalysisStore(_database);
        _settings = new SqliteAppSettingStore(_database);
        var runner = new StubAnalysisRunner(_store);
        var restarted = new AnalysisRpcTarget(
            _store,
            runner,
            _settings,
            new RunEventNotifier(new SilentNotifier(), NullLogger<RunEventNotifier>.Instance),
            new AnalysisFreshnessChecker(Git, TimeProvider.System),
            NullLogger<AnalysisRpcTarget>.Instance);

        var view = await restarted.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.HasAnalysis.ShouldBeTrue();
        view.Nodes.Count.ShouldBe(2);
        runner.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task An_analysis_opens_in_dependency_flow_and_says_which_groupings_it_holds()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.Grouping.ShouldBe(AnalysisGroupingMode.Dependency_flow);
        view.AvailableGroupings.ShouldBe(
            [AnalysisGroupingMode.Dependency_flow, AnalysisGroupingMode.Change_clusters],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Switching_grouping_shows_the_same_nodes_arranged_differently_and_runs_nothing()
    {
        // Iteration 11 verification steps 1 and 5, together, because they are the same claim seen
        // from two sides: the node set is identical, and the switch cost nothing because both
        // groupings came out of the run that was already paid for.
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        var runsAfterAnalysing = _runner.Runs;

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        clusters.Grouping.ShouldBe(AnalysisGroupingMode.Change_clusters);
        clusters.AnalysisId.ShouldBe(flow.AnalysisId);

        // Identical node sets, asserted as set equality rather than by counting.
        Ids(clusters).ShouldBe(Ids(flow), ignoreOrder: true);

        // And a different arrangement of them, or there would be nothing to switch to.
        clusters.Containers.Count.ShouldBe(2);
        flow.Containers.Count.ShouldBe(1);

        // Switching back, and switching again, still runs nothing.
        await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Dependency_flow, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        _runner.Runs.ShouldBe(runsAfterAnalysing);
    }

    [Fact]
    public async Task Every_node_of_the_changeset_is_in_both_projections()
    {
        // §0.2.5 for both pictures. The sample's changeset has three files and its answer covers two
        // of them, so this compares the projections against the answer rather than against the
        // changeset — the changeset half is AnalysisValidator's, and it is checked there.
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        foreach (var view in new[] { flow, clusters })
        {
            var members = view.Containers
                .SelectMany(static container => container.NodeIds)
                .ToHashSet(StringComparer.Ordinal);

            members.SetEquals(Ids(view)).ShouldBeTrue();

            // And every node knows which container it is in, in this grouping.
            view.Nodes.ShouldAllBe(node => node.ContainerId.Length > 0);
        }
    }

    [Fact]
    public async Task The_same_edge_crosses_containers_in_one_grouping_and_not_in_the_other()
    {
        // What change clusters costs and dependency flow buys, as a single boolean. The host computes
        // it from membership, so it moves with the grouping rather than being claimed by the model.
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        flow.Edges.ShouldHaveSingleItem().CrossesContainers.ShouldBeFalse();

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        clusters.Edges.ShouldHaveSingleItem().CrossesContainers.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_grouping_gets_its_own_entry_points_ranks_and_reading_order()
    {
        // Requirement 5. None of the three is in the model's document any more — one integer and one
        // state could not have described two groupings — so all three are read off the grouping being
        // projected.
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        flow.ReadingOrder.ShouldBe(["src/Contract.cs", "src/Caller.cs"]);
        flow.Nodes.Single(static node => node.Id == "src/Caller.cs").Rank.ShouldBe(2);
        flow.Nodes.Single(static node => node.Id == "src/Contract.cs").States
            .ShouldContain(AnalysisNodeInfoState.Entry_point);
        flow.Nodes.Single(static node => node.Id == "src/Caller.cs").States
            .ShouldNotContain(AnalysisNodeInfoState.Entry_point);

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        clusters.ReadingOrder.ShouldBe(["src/Caller.cs", "src/Contract.cs"]);

        // Every cluster here holds one node, so every node starts one.
        clusters.Nodes.ShouldAllBe(node => node.Rank == 1);
        clusters.Nodes.ShouldAllBe(node => node.States.Contains(AnalysisNodeInfoState.Entry_point));
    }

    [Fact]
    public async Task The_statistics_that_depend_on_the_grouping_move_and_the_rest_do_not()
    {
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        flow.Statistics.ShouldNotBeNull().ContainerCount.ShouldBe(1);
        clusters.Statistics.ShouldNotBeNull().ContainerCount.ShouldBe(2);

        clusters.Statistics.NodeCount.ShouldBe(flow.Statistics.NodeCount);
        clusters.Statistics.TotalFiles.ShouldBe(flow.Statistics.TotalFiles);
        clusters.Statistics.EdgeCount.ShouldBe(flow.Statistics.EdgeCount);
    }

    [Fact]
    public async Task A_warning_about_one_grouping_is_shown_with_that_grouping_and_not_the_other()
    {
        // A gap in a cluster the reviewer is not looking at is something they cannot act on, and a
        // reading of the diagram in front of them that is simply untrue.
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        flow.Diagnostics.Select(static d => d.Code).ShouldBe([AnalysisDiagnosticCodes.Cycle]);

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        clusters.Diagnostics.Select(static d => d.Code).ShouldBe(
            [AnalysisDiagnosticCodes.Cycle, AnalysisDiagnosticCodes.UnreachableNode],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Reviewed_marks_survive_a_change_of_grouping()
    {
        // Requirement 6, and the reason the whole thing was affordable: marks are node ids on the
        // analysis row, and nothing about a grouping touches them.
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        await _target.SetReviewedAsync(
            new SetNodesReviewedRequest(analysisId: null, nodeIds: ["src/Caller.cs"], repositoryPath: "/repo", reviewed: true),
            TestContext.Current.CancellationToken);

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        clusters.ReviewedNodeIds.ShouldBe(["src/Caller.cs"]);

        // And the mark still lands on the node it was made against, not on a position in a list.
        clusters.Nodes.ShouldContain(node => node.Id == "src/Caller.cs");
    }

    [Fact]
    public async Task The_chosen_grouping_is_remembered_for_that_analysis_across_a_restart()
    {
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        var restarted = await Restart();

        var view = await restarted.Target.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.Grouping.ShouldBe(AnalysisGroupingMode.Change_clusters);
        restarted.Runner.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task An_analysis_that_does_not_hold_a_grouping_refuses_it_by_name()
    {
        // The alternative would be to quietly show dependency flow while the control read "change
        // clusters", which the reviewer would have no way to see through.
        await _target.RunAsync(Request(changeClusters: false), TestContext.Current.CancellationToken);

        var view = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.AvailableGroupings.ShouldBe([AnalysisGroupingMode.Dependency_flow]);

        var failure = await Should.ThrowAsync<LocalRpcException>(async () => await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();

        data.Code.ShouldBe("analysis_grouping_unavailable");
        data.Args.ShouldNotBeNull()["grouping"].ShouldBe("change_clusters");
    }

    [Fact]
    public async Task Switching_a_repository_that_has_never_been_analysed_says_so()
    {
        var failure = await Should.ThrowAsync<LocalRpcException>(async () => await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe("analysis_not_found");
    }

    [Fact]
    public async Task What_a_run_should_ask_for_is_remembered_and_reported_back()
    {
        // The one control that changes what an analysis costs, so it comes back the way it was left
        // rather than resetting to the expensive answer every time the screen opens.
        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .ProduceChangeClusters.ShouldBeTrue("both groupings unless the reviewer said otherwise.");

        await _target.RunAsync(Request(changeClusters: false), TestContext.Current.CancellationToken);

        _runner.LastOptions.ShouldNotBeNull().ChangeClusters.ShouldBeFalse();

        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .ProduceChangeClusters.ShouldBeFalse();

        // Absent means "what you remembered", so a caller that does not know about the field cannot
        // silently change what a run costs.
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        _runner.LastOptions.ShouldNotBeNull().ChangeClusters.ShouldBeFalse();
    }

    [Fact]
    public async Task Whether_a_run_asks_for_implementation_groups_is_remembered_on_its_own()
    {
        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .ProduceImplementationGroups.ShouldBeTrue("asked for unless the reviewer said otherwise.");

        await _target.RunAsync(Request(implementationGroups: false), TestContext.Current.CancellationToken);

        _runner.LastOptions.ShouldNotBeNull().ImplementationGroups.ShouldBeFalse();
        _runner.LastOptions.ChangeClusters.ShouldBeTrue("the two choices are independent.");

        var view = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        view.ProduceImplementationGroups.ShouldBeFalse();
        view.ProduceChangeClusters.ShouldBeTrue();

        // Absent means remembered, exactly as for the other choice.
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        _runner.LastOptions.ShouldNotBeNull().ImplementationGroups.ShouldBeFalse();
    }

    [Fact]
    public async Task Implementation_groups_are_projected_onto_the_grouping_on_screen()
    {
        // Dependency flow keeps the two members in one cluster, so the group is there to be drawn as
        // one box; change clusters splits them, so in that picture there is nothing to merge.
        var flow = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        flow.ImplementationGroupsProduced.ShouldBeTrue();

        var group = flow.ImplementationGroups.ShouldHaveSingleItem();

        group.AbstractionNodeId.ShouldBe("src/Contract.cs");
        group.ImplementationNodeIds.ShouldBe(["src/Caller.cs"]);
        group.ContainerId.ShouldBe("core");

        var clusters = await _target.SetGroupingAsync(
            new SetGroupingRequest(analysisId: null, grouping: SetGroupingMode.Change_clusters, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        clusters.ImplementationGroups.ShouldBeEmpty();
        clusters.ImplementationGroupsProduced.ShouldBeTrue("asked for, and simply split in this grouping.");
    }

    [Fact]
    public async Task An_analysis_that_was_not_asked_for_implementation_groups_says_so()
    {
        // Null in the document, not empty: "nobody asked" and "there were none" are different things
        // to tell a reviewer, and only the document knows which it is.
        var view = await _target.RunAsync(Request(implementationGroups: false), TestContext.Current.CancellationToken);

        view.ImplementationGroupsProduced.ShouldBeFalse();
        view.ImplementationGroups.ShouldBeEmpty();

        // And the same survives being stored and read back.
        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .ImplementationGroupsProduced.ShouldBeFalse();
    }

    [Fact]
    public async Task A_new_analysis_has_nothing_marked_reviewed()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.ReviewedNodeIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Marking_nodes_reviewed_answers_with_every_mark_and_the_analysis_it_belongs_to()
    {
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var state = await _target.SetReviewedAsync(
            new SetNodesReviewedRequest(analysisId: null, nodeIds: ["src/Caller.cs"], repositoryPath: "/repo", reviewed: true),
            TestContext.Current.CancellationToken);

        state.AnalysisId.ShouldBe(view.AnalysisId);
        state.ReviewedNodeIds.ShouldBe(["src/Caller.cs"]);

        // And the next read of the whole view agrees, so the interface cannot end up showing marks
        // the stored analysis does not have.
        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .ReviewedNodeIds.ShouldBe(["src/Caller.cs"]);
    }

    [Fact]
    public async Task A_node_that_is_not_in_the_analysis_is_refused_by_name_and_nothing_is_written()
    {
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var failure = await Should.ThrowAsync<LocalRpcException>(async () => await _target.SetReviewedAsync(
            new SetNodesReviewedRequest(
                analysisId: null,
                // One good id and one invented one. The whole call is refused rather than half
                // applied: a partly-applied mark is a state the reviewer would have to find by
                // counting.
                nodeIds: ["src/Caller.cs", "src/Invented.cs"],
                repositoryPath: "/repo",
                reviewed: true),
            TestContext.Current.CancellationToken));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();

        data.Code.ShouldBe("analysis_node_not_found");
        data.Args.ShouldNotBeNull()["nodeId"].ShouldBe("src/Invented.cs");

        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .ReviewedNodeIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Marking_a_repository_that_has_never_been_analysed_says_so()
    {
        var failure = await Should.ThrowAsync<LocalRpcException>(async () => await _target.SetReviewedAsync(
            new SetNodesReviewedRequest(analysisId: null, nodeIds: ["src/Caller.cs"], repositoryPath: "/repo", reviewed: true),
            TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe("analysis_not_found");
    }

    [Fact]
    public async Task The_library_lists_every_run_most_recent_first_with_what_a_reviewer_chooses_by()
    {
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var library = await _target.ListAsync(Request(), TestContext.Current.CancellationToken);

        library.RetentionLimit.ShouldBe(AnalysisLibraryPolicy.RetentionLimit);
        library.Entries.Select(static entry => entry.AnalysisId).ShouldBe(["analysis3", "analysis2", "analysis1"]);
        library.Entries.Select(static entry => entry.IsLatest).ShouldBe([true, false, false]);

        var entry = library.Entries[0];

        entry.Model.ShouldBe("gpt-4o");
        entry.ProviderDisplayName.ShouldBe("Test");
        entry.CostUsd.ShouldBe("0.25");
        entry.CreatedAtUtc.ShouldBe(DateTimeOffset.UnixEpoch.AddMinutes(3));
        entry.DurationMs.ShouldBe(42_000);
        entry.InputTokens.ShouldBe(1000);
        entry.OutputTokens.ShouldBe(200);
        entry.FileCount.ShouldBe(3);
        entry.LinesAdded.ShouldBe(16);
        entry.LinesRemoved.ShouldBe(4);
        entry.NodeCount.ShouldBe(2);
        entry.ContainerCount.ShouldBe(1);
        entry.ToolCallCount.ShouldBe(3);
        entry.HeadCommit.ShouldBe("head0001");
    }

    [Fact]
    public async Task An_earlier_run_reopens_by_id_as_itself_and_costs_nothing()
    {
        // Requirement 8: reopen any run instantly without re-running. Asserted from the one count that
        // could prove it — how many times the runner was asked.
        var first = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        var runs = _runner.Runs;

        var reopened = await _target.GetAsync(
            Request(analysisId: first.AnalysisId),
            TestContext.Current.CancellationToken);

        reopened.AnalysisId.ShouldBe(first.AnalysisId);
        reopened.IsLatest.ShouldBeFalse();
        reopened.CreatedAtUtc.ShouldBe(first.CreatedAtUtc);
        _runner.Runs.ShouldBe(runs);

        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken)).IsLatest.ShouldBeTrue();
    }

    [Fact]
    public async Task Grouping_and_reviewed_marks_land_on_the_run_that_was_reopened_not_the_latest()
    {
        var first = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        var second = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var regrouped = await _target.SetGroupingAsync(
            new SetGroupingRequest(
                analysisId: first.AnalysisId,
                grouping: SetGroupingMode.Change_clusters,
                repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        regrouped.AnalysisId.ShouldBe(first.AnalysisId);
        regrouped.IsLatest.ShouldBeFalse();

        var marks = await _target.SetReviewedAsync(
            new SetNodesReviewedRequest(
                analysisId: first.AnalysisId,
                nodeIds: ["src/Caller.cs"],
                repositoryPath: "/repo",
                reviewed: true),
            TestContext.Current.CancellationToken);

        marks.AnalysisId.ShouldBe(first.AnalysisId);

        var latest = await _target.GetAsync(Request(), TestContext.Current.CancellationToken);

        latest.AnalysisId.ShouldBe(second.AnalysisId);
        latest.ReviewedNodeIds.ShouldBeEmpty();

        var earlier = await _target.GetAsync(
            Request(analysisId: first.AnalysisId),
            TestContext.Current.CancellationToken);

        earlier.ReviewedNodeIds.ShouldBe(["src/Caller.cs"]);
        earlier.Grouping.ShouldBe(AnalysisGroupingMode.Change_clusters);
    }

    [Fact]
    public async Task An_id_from_another_repository_is_not_found_rather_than_read()
    {
        var elsewhere = AnalysisSamples.Completed("/other") with { Id = "elsewhere" };
        await _store.SaveAsync(elsewhere, TestContext.Current.CancellationToken);

        var failure = await Should.ThrowAsync<LocalRpcException>(async () => await _target.GetAsync(
            Request(analysisId: "elsewhere"),
            TestContext.Current.CancellationToken));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();

        data.Code.ShouldBe("analysis_not_found");
        data.Args.ShouldNotBeNull()["analysisId"].ShouldBe("elsewhere");

        await Should.ThrowAsync<LocalRpcException>(async () => await _target.DeleteAsync(
            new AnalysisRefRequest(analysisId: "elsewhere", repositoryPath: "/repo"),
            TestContext.Current.CancellationToken));

        (await _store.FindAsync("elsewhere", TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_trace_is_every_tool_call_in_the_order_the_model_made_them()
    {
        // Requirement 7. The sample stores its calls out of order, so this proves the trace is sorted
        // by ordinal rather than read back in whatever order the array was written.
        var view = await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        view.ToolCallCount.ShouldBe(3);

        var trace = await _target.TraceAsync(
            new AnalysisRefRequest(analysisId: view.AnalysisId!, repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        trace.AnalysisId.ShouldBe(view.AnalysisId);
        trace.ToolCalls.Select(static call => call.Ordinal).ShouldBe([1, 2, 3]);
        trace.ToolCalls.Select(static call => call.ToolName)
            .ShouldBe(["get_project_profile", "get_file_diff", "read_file"]);
        trace.ToolCalls.Select(static call => call.ResultBytes).ShouldBe([512, 2048, 40]);
        trace.ToolCalls[2].IsError.ShouldBeTrue();
        trace.ToolCalls[2].Turn.ShouldBe(2);
        trace.ToolCalls[1].DurationMs.ShouldBe(12);
        trace.ProgressMessages.ShouldBe(["Reading the contract"]);
        _runner.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_run_removes_it_alone_and_answers_with_the_library_as_it_stands()
    {
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);
        await _target.RunAsync(Request(), TestContext.Current.CancellationToken);

        var library = await _target.DeleteAsync(
            new AnalysisRefRequest(analysisId: "analysis2", repositoryPath: "/repo"),
            TestContext.Current.CancellationToken);

        library.Entries.ShouldHaveSingleItem().AnalysisId.ShouldBe("analysis1");
        library.Entries[0].IsLatest.ShouldBeTrue();

        // And the screen's default read now opens the one that is left.
        (await _target.GetAsync(Request(), TestContext.Current.CancellationToken))
            .AnalysisId.ShouldBe("analysis1");
    }

    [Fact]
    public async Task A_stored_analysis_is_fresh_until_the_working_tree_moves_and_fresh_again_once_reverted()
    {
        // Requirement 9's verification step 6, against a real repository: change a file after the
        // run and the analysis is stale; revert the change and it is not.
        using var repository = TemporaryRepository.CreateWithCommit();
        repository.Write("src/Contract.cs", "class Contract {}\n");
        repository.Write("src/Caller.cs", "class Caller {}\n");
        repository.Commit("initial");

        repository.Write("src/Contract.cs", "class Contract { int Tenant; }\n");
        repository.Write("src/Caller.cs", "class Caller { void Call() {} }\n");

        var analysis = await StoreAnalysisOfAsync(repository);
        var request = new AnalysisRefRequest(analysisId: analysis.Id, repositoryPath: repository.Root);

        var fresh = await _target.CheckFreshnessAsync(request, TestContext.Current.CancellationToken);

        fresh.IsStale.ShouldBeFalse();
        fresh.Basis.ShouldBe(Contracts.AnalysisFreshnessBasis.Content);

        // Same line counts, different content: only a content hash can see this edit.
        repository.Write("src/Contract.cs", "class Contract { int Tenent; }\n");

        var edited = await _target.CheckFreshnessAsync(request, TestContext.Current.CancellationToken);

        edited.IsStale.ShouldBeTrue();
        edited.HeadMoved.ShouldBeFalse();
        edited.ModifiedPaths.ShouldBe(["src/Contract.cs"]);
        edited.ModifiedCount.ShouldBe(1);

        repository.Write("src/Contract.cs", "class Contract { int Tenant; }\n");

        (await _target.CheckFreshnessAsync(request, TestContext.Current.CancellationToken))
            .IsStale.ShouldBeFalse("reverting the edit makes the analysis describe the tree again.");

        repository.Write("src/New.cs", "class New {}\n");

        var added = await _target.CheckFreshnessAsync(request, TestContext.Current.CancellationToken);

        added.AddedPaths.ShouldBe(["src/New.cs"]);
        added.IsStale.ShouldBeTrue();

        _runner.Runs.ShouldBe(0);
    }

    [Fact]
    public async Task Committing_the_change_moves_head_and_empties_the_changeset_so_the_analysis_is_stale()
    {
        using var repository = TemporaryRepository.CreateWithCommit();
        repository.Write("src/Contract.cs", "class Contract {}\n");
        repository.Commit("initial");
        repository.Write("src/Contract.cs", "class Contract { int Tenant; }\n");

        var analysis = await StoreAnalysisOfAsync(repository);

        repository.Commit("the change itself");

        var report = await _target.CheckFreshnessAsync(
            new AnalysisRefRequest(analysisId: analysis.Id, repositoryPath: repository.Root),
            TestContext.Current.CancellationToken);

        report.IsStale.ShouldBeTrue();
        report.HeadMoved.ShouldBeTrue();
        report.RemovedPaths.ShouldBe(["src/Contract.cs"]);
        report.CurrentHeadCommit.ShouldBe(repository.HeadSha());
        report.RecordedHeadCommit.ShouldBe(analysis.HeadCommit);
    }

    /// <summary>
    /// Stores an analysis of the repository's current change the way the runner would: the changeset
    /// read with content hashes, and HEAD as it stands. The document is the sample's — freshness is a
    /// question about the changeset, not about what the model said.
    /// </summary>
    private async Task<Analysis> StoreAnalysisOfAsync(TemporaryRepository repository)
    {
        var changeset = await Git.GetChangesetAsync(
            new ChangesetQuery(repository.Root, HashContent: true),
            TestContext.Current.CancellationToken);

        var analysis = AnalysisSamples.Completed(repository.Root) with
        {
            Id = "fresh" + Guid.NewGuid().ToString("N"),
            HeadCommit = repository.HeadSha(),
            ChangedFiles = [.. changeset.Files.Select(ChangedFileFacts.From)],
        };

        await _store.SaveAsync(analysis, TestContext.Current.CancellationToken);

        return analysis;
    }

    private static GitClient CreateGit()
    {
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);

        return new GitClient(
            runner,
            new GitEnvironment(runner, NullLogger<GitEnvironment>.Instance),
            NullLogger<GitClient>.Instance);
    }

    private AnalysisRpcTarget Target() => new(
        _store,
        _runner,
        _settings,
        new RunEventNotifier(new SilentNotifier(), NullLogger<RunEventNotifier>.Instance),
        new AnalysisFreshnessChecker(Git, TimeProvider.System),
        NullLogger<AnalysisRpcTarget>.Instance);

    private static AnalysisRequest Request(
        bool? changeClusters = null,
        bool? implementationGroups = null,
        string? analysisId = null) =>
        new(
            analysisId: analysisId,
            changeClusters: changeClusters,
            implementationGroups: implementationGroups,
            repositoryPath: "/repo");

    private static HashSet<string> Ids(AnalysisView view) =>
        [.. view.Nodes.Select(static node => node.Id)];

    /// <summary>
    /// Closes the database and builds the whole surface again over the same file, the way starting
    /// the application does. The runner is fresh, so its run count answers "did reopening this cost
    /// anything?".
    /// </summary>
    private async Task<(AnalysisRpcTarget Target, StubAnalysisRunner Runner)> Restart()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        _database = new AppDatabase(
            Path.Combine(_dataDirectory.FullName, "diffhacker.db"),
            NullLogger<AppDatabase>.Instance);

        _store = new SqliteAnalysisStore(_database);
        _settings = new SqliteAppSettingStore(_database);

        var runner = new StubAnalysisRunner(_store);

        return (
            new AnalysisRpcTarget(
                _store,
                runner,
                _settings,
                new RunEventNotifier(new SilentNotifier(), NullLogger<RunEventNotifier>.Instance),
                new AnalysisFreshnessChecker(Git, TimeProvider.System),
                NullLogger<AnalysisRpcTarget>.Instance),
            runner);
    }

    /// <summary>
    /// Stands in for the whole pipeline. The orchestration itself is covered by
    /// <c>AnalysisRunnerTests</c>; what is being tested here is the surface above it.
    /// </summary>
    private sealed class StubAnalysisRunner(SqliteAnalysisStore store) : IAnalysisRunner
    {
        public int Runs { get; private set; }

        public string? FailWith { get; set; }

        public Exception? Throw { get; set; }

        public AnalysisRunOptions? LastOptions { get; private set; }

        public async Task<AnalysisRunResult> RunAsync(
            string repositoryPath,
            AnalysisRunOptions options,
            IProgress<LlmRunEvent>? progress,
            CancellationToken cancellationToken)
        {
            Runs++;
            LastOptions = options;

            if (Throw is { } thrown)
            {
                throw thrown;
            }

            if (FailWith is { } code)
            {
                return new AnalysisRunResult
                {
                    FailureCode = code,
                    Diagnostics = code == AnalysisFailures.ValidationFailed
                        ?
                        [
                            AnalysisDiagnostic.Error(
                                AnalysisDiagnosticCodes.FileNotCovered,
                                "src/Dropped.cs",
                                "No node covers the changed file 'src/Dropped.cs'."),
                        ]
                        : [],
                };
            }

            // A distinct id and timestamp per run, because a re-run is a new row: the real runner
            // makes a fresh Guid, and a stub that reused one id could not be asked to run twice.
            var analysis = AnalysisSamples.Completed(
                repositoryPath,
                options.ChangeClusters,
                options.ImplementationGroups) with
            {
                Id = $"analysis{Runs}",
                CreatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(Runs),
            };
            await store.SaveAsync(analysis, cancellationToken);

            return new AnalysisRunResult { Analysis = analysis, Usage = analysis.Usage };
        }
    }

    private sealed class SilentNotifier : IRpcNotifier
    {
        public Task NotifyAsync(string method, object payload, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
