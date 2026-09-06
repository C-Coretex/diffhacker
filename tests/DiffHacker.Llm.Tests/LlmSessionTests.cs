using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// The tool-calling loop, which is what Iteration 4 exists to build.
/// <para>
/// Requirement 1 asks for a multi-turn loop ending in a structured response, and requirement 2
/// for multiple tool calls in one turn where the provider supports it. Everything here drives
/// a scripted provider: requirement 9 forbids reaching a real one, and a loop that only worked
/// against a live model would be untestable by definition.
/// </para>
/// </summary>
public sealed class LlmSessionTests
{
    [Fact]
    public async Task A_plain_answer_ends_the_run_in_one_turn()
    {
        var harness = new SessionHarness();
        harness.Provider.Says("It renames a method.");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.Text.ShouldBe("It renames a method.");
        result.TurnCount.ShouldBe(1);
        result.ToolCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_tool_calls_in_one_turn_are_both_dispatched()
    {
        // The canned conversation from the iteration's own verification steps: call the echo
        // tool twice, then answer.
        var calls = new List<string>();
        var harness = new SessionHarness();

        harness.Provider
            .Calls(("echo", new { text = "first" }), ("echo", new { text = "second" }))
            .Says("done");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool(calls.Add)]),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        calls.Count.ShouldBe(2, "both calls in the turn must run, not just the first.");
        calls[0].ShouldContain("first");
        calls[1].ShouldContain("second");
        result.TurnCount.ShouldBe(2);
    }

    [Fact]
    public async Task Tool_calls_are_traced_in_the_order_the_model_asked_for_them()
    {
        var harness = new SessionHarness();

        harness.Provider
            .Calls(("echo", new { text = "one" }), ("echo", new { text = "two" }))
            .Calls(("echo", new { text = "three" }))
            .Says("done");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            progress: null,
            TestContext.Current.CancellationToken);

        // Iteration 13's inspector renders this list. Concurrent dispatch must not scramble it.
        result.ToolCalls.Select(call => call.Ordinal).ShouldBe([1, 2, 3]);
        result.ToolCalls.Select(call => call.Turn).ShouldBe([1, 1, 2]);
        result.ToolCalls[0].ArgumentsPreview.ShouldContain("one");
        result.ToolCalls[1].ArgumentsPreview.ShouldContain("two");
        result.ToolCalls[2].ArgumentsPreview.ShouldContain("three");
        result.ToolCalls.ShouldAllBe(call => call.ResultBytes > 0);
    }

    [Fact]
    public async Task Usage_is_reported_per_request_and_cumulatively()
    {
        var harness = new SessionHarness();

        harness.Provider
            .Calls(("echo", new { text = "x" }))
            .Responds(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "done"))
            {
                Usage = FakeChatClient.Usage(100, 50),
                FinishReason = Microsoft.Extensions.AI.ChatFinishReason.Stop,
            });

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            progress: null,
            TestContext.Current.CancellationToken);

        session.RequestUsages.Count.ShouldBe(2, "one entry per round trip to the provider.");
        session.RequestUsages[1].InputTokens.ShouldBe(100);
        session.RequestUsages[1].OutputTokens.ShouldBe(50);

        result.Usage.InputTokens.ShouldBe(120, "20 from the tool turn plus 100 from the answer.");
        result.Usage.OutputTokens.ShouldBe(58);
        result.Usage.TotalTokens.ShouldBe(178);
        result.Usage.IsReported.ShouldBeTrue();
    }

    [Fact]
    public async Task Cost_is_estimated_where_a_rate_is_known()
    {
        var harness = new SessionHarness();
        harness.Provider.Says("done", FakeChatClient.Usage(1_000_000, 1_000_000));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            progress: null,
            TestContext.Current.CancellationToken);

        // The harness prices input at $1000 and output at $2000 per million.
        result.Usage.CostIsKnown.ShouldBeTrue();
        result.Usage.EstimatedCostUsd.ShouldBe(3_000m);
    }

    [Fact]
    public async Task An_unpriced_model_reports_unknown_cost_rather_than_zero()
    {
        var harness = new SessionHarness
        {
            Profile = SessionHarness.ProfileFor(LlmProviderType.OpenAi, model: "unpriced-model"),
        };

        harness.Provider.Says("done", FakeChatClient.Usage(1_000, 1_000));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Usage.TotalTokens.ShouldBe(2_000, "the tokens are still known.");
        result.Usage.CostIsKnown.ShouldBeFalse(
            "a zero would read as 'this was free', which is a different and false claim.");
        result.Usage.EstimatedCostUsd.ShouldBeNull();
    }

    [Fact]
    public async Task A_profile_rate_overrides_the_price_table()
    {
        var harness = new SessionHarness
        {
            Profile = SessionHarness.ProfileFor(LlmProviderType.OpenAi) with
            {
                InputCostPerMillion = 10m,
                OutputCostPerMillion = 20m,
            },
        };

        harness.Provider.Says("done", FakeChatClient.Usage(1_000_000, 1_000_000));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Usage.EstimatedCostUsd.ShouldBe(
            30m,
            "the user's own rate is the one that is not out of date.");
    }

    [Fact]
    public async Task A_structured_answer_is_validated_against_its_schema()
    {
        var harness = new SessionHarness();
        harness.Provider.Says("""{"summary":"a rename","confidence":4}""");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.StructuredJson.ShouldNotBeNull();
        result.StructuredJson!.ShouldContain("\"summary\"");
    }

    [Fact]
    public async Task A_fenced_answer_is_unwrapped_rather_than_rejected()
    {
        // Models fence their JSON even when told not to. Failing the run over a code fence
        // would cost a full repair round trip for nothing.
        var harness = new SessionHarness();
        harness.Provider.Says("""
            ```json
            {"summary":"a rename","confidence":4}
            ```
            """);

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.StructuredJson.ShouldNotBeNull();
        result.StructuredJson!.ShouldNotContain("```");
    }

    [Fact]
    public async Task A_schema_violation_gets_one_repair_attempt()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Says("""{"summary":"a rename"}""")
            .Says("""{"summary":"a rename","confidence":4}""");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        harness.Provider.Requests.Count.ShouldBe(2);
        harness.Provider.LastRequest.Messages[^1].Text.ShouldContain(
            "did not match",
            Case.Insensitive,
            "the model is told what was wrong, which works far better than asking again.");
    }

    [Fact]
    public async Task A_second_schema_violation_fails_the_run()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Says("""{"summary":"a rename"}""")
            .Says("""{"summary":"still wrong"}""");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Failed);
        result.FailureCode.ShouldBe(LlmFailures.InvalidResponse);
        harness.Provider.Requests.Count.ShouldBe(2, "exactly one repair, not an endless argument.");
    }

    [Fact]
    public async Task An_unknown_tool_name_is_answered_rather_than_thrown()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Calls(("invented", new { text = "x" }))
            .Says("sorry");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.ToolCalls.ShouldHaveSingleItem().IsError.ShouldBeTrue();

        // The correction goes back as a tool result, so the model can retry with a real name.
        harness.Provider.LastRequest.ToolResults
            .ShouldHaveSingleItem()
            .ShouldContain("no tool named");
    }

    [Fact]
    public async Task A_tool_that_throws_becomes_an_error_result_not_a_dead_run()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Calls(("broken", new { }))
            .Says("recovered");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.ThrowingTool()]),
            progress: null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.ToolCalls.ShouldHaveSingleItem().IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Progress_events_describe_turns_and_tool_calls()
    {
        var harness = new SessionHarness();
        harness.Provider
            .Calls(("echo", new { text = "x" }))
            .Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.Count(e => e.Kind == LlmRunEventKind.TurnStarted).ShouldBe(2);
        harness.Events.Count(e => e.Kind == LlmRunEventKind.TurnFinished).ShouldBe(2);
        harness.Events.ShouldContain(e => e.Kind == LlmRunEventKind.ToolCallStarted && e.ToolName == "echo");

        var finished = harness.Events.Single(e => e.Kind == LlmRunEventKind.ToolCallFinished);
        finished.ResultBytes.ShouldNotBeNull();
        finished.Duration.ShouldNotBeNull();

        // Every event carries the running total, so a live view never has to accumulate.
        harness.Events[^1].CumulativeUsage.TotalTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_session_runs_once()
    {
        var harness = new SessionHarness();
        harness.Provider.Says("done").Says("again");

        await using var session = harness.Build();
        await session.RunAsync(SessionHarness.Conversation(), null, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            session.RunAsync(SessionHarness.Conversation(), null, TestContext.Current.CancellationToken));
    }
}
