using DiffHacker.Core.Llm;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// The hard stops.
/// <para>
/// A tool-using loop can fail in a way a single request cannot: the model keeps calling tools,
/// learns nothing, and spends real money doing it. What matters as much as stopping is what the
/// stop <i>says</i> — §0.2.5 and §0.2.8 both forbid presenting a partial result as a complete
/// one, so every case here checks the explanation as well as the outcome.
/// </para>
/// </summary>
public sealed class LlmBudgetTests
{
    [Fact]
    public async Task The_tool_call_limit_stops_the_run()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxToolCalls = 3 } };

        for (var i = 0; i < 10; i++)
        {
            harness.Provider.Calls(("echo", new { text = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        result.FailureCode.ShouldBe(LlmFailures.BudgetExceeded);
        result.ToolCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_turn_limit_stops_the_run()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxTurns = 2 } };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Calls(("echo", new { text = "b" }))
            .Calls(("echo", new { text = "c" }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        harness.Provider.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_token_limit_stops_the_run()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxTotalTokens = 50 } };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Calls(("echo", new { text = "b" }))
            .Calls(("echo", new { text = "c" }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        result.Usage.TotalTokens.ShouldBeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task A_cost_ceiling_stops_the_run_when_one_is_set()
    {
        // Off by default, because killing a run mid-flight throws away everything already paid
        // for. Available for the user who would rather have that than a surprise.
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxCostUsd = 0.01m } };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Calls(("echo", new { text = "b" }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        result.ProviderMessage.ShouldNotBeNull();
        result.ProviderMessage!.ShouldContain("cost ceiling");
    }

    [Fact]
    public async Task Tools_failing_over_and_over_stop_the_run()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxConsecutiveToolFailures = 2 } };

        harness.Provider
            .Calls(("broken", new { }))
            .Calls(("broken", new { }))
            .Calls(("broken", new { }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.ThrowingTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        result.ProviderMessage!.ShouldContain(
            "in a row",
            Case.Insensitive,
            "two failures in a row means the model is not reading the error it is handed.");
    }

    [Fact]
    public async Task A_hard_stop_explains_what_was_and_was_not_produced()
    {
        // §0.2.8: a partial result is never presented as a complete one. That needs the reader
        // to know which limit fired and how far the run got.
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxToolCalls = 1 } };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Calls(("echo", new { text = "b" }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.StructuredJson.ShouldBeNull("there is no answer, so none is offered.");

        var explanation = result.ProviderMessage.ShouldNotBeNull();
        explanation.ShouldContain("1 tool call");
        explanation.ShouldContain("token");
        explanation.ShouldContain("no final answer");
    }

    [Fact]
    public async Task A_reached_limit_is_a_hard_stop_when_nothing_is_asked_to_decide()
    {
        // The default, and every caller written before this existed: no callback means no
        // behaviour change from a plain hard stop.
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxToolCalls = 1 } };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Calls(("echo", new { text = "b" }));

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        result.ToolCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Continuing_past_a_reached_limit_raises_it_by_the_original_amount_and_keeps_going()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxToolCalls = 2 } };

        for (var i = 0; i < 5; i++)
        {
            harness.Provider.Calls(("echo", new { text = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }

        harness.Provider.Says("done");

        var asked = new List<LlmBudgetLimitReached>();

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken,
            (limit, _) =>
            {
                asked.Add(limit);
                return Task.FromResult(BudgetDecision.Continue);
            });

        // One prompt per limit reached: 2, then 4 (2 + the original 2), by which point the fifth
        // call and the closing turn both fit under the raised ceiling.
        asked.Count.ShouldBe(2);
        asked[0].Limit.ShouldBe(LlmBudgetLimit.ToolCalls);
        asked[0].ToolCallsUsed.ShouldBe(2);
        asked[1].ToolCallsUsed.ShouldBe(4);
        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
        result.ToolCalls.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Stopping_when_asked_produces_the_same_partial_result_a_hard_stop_would()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxToolCalls = 2 } };

        for (var i = 0; i < 5; i++)
        {
            harness.Provider.Calls(("echo", new { text = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken,
            (_, _) => Task.FromResult(BudgetDecision.Stop));

        result.Outcome.ShouldBe(LlmRunOutcome.BudgetExceeded);
        result.FailureCode.ShouldBe(LlmFailures.BudgetExceeded);
        result.ToolCalls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_turn_limit_can_also_be_continued_past()
    {
        var harness = new SessionHarness { Budget = LlmBudget.Default with { MaxTurns = 1 } };

        harness.Provider
            .Calls(("echo", new { text = "a" }))
            .Says("done");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            null,
            TestContext.Current.CancellationToken,
            (limit, _) =>
            {
                limit.Limit.ShouldBe(LlmBudgetLimit.Turns);
                return Task.FromResult(BudgetDecision.Continue);
            });

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);
    }

    [Fact]
    public void The_defaults_are_sized_for_a_large_changeset()
    {
        // These are user-visible behaviour, so the numbers are pinned rather than assumed.
        var budget = LlmBudget.Default;

        budget.MaxToolCalls.ShouldBe(500);
        budget.MaxTurns.ShouldBe(300);
        budget.MaxTotalTokens.ShouldBe(
            10_000_000,
            "every turn resends the conversation, so 300 turns of a 1000-file exploration reaches "
            + "the millions without anything having gone wrong; a guard that stops a working run "
            + "is not a runaway guard.");
        budget.MaxRetryAttempts.ShouldBe(5);
        budget.MaxConsecutiveToolFailures.ShouldBe(3);
        budget.RequestTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        budget.MaxCostUsd.ShouldBeNull(
            "a mid-run cost kill wastes everything already spent; Iteration 13's pre-run estimate is the place to prevent an expensive run.");
        budget.ToolResultRetentionBytes.ShouldBe(200 * 1024);
    }

    [Fact]
    public async Task Old_tool_results_are_pruned_once_they_outgrow_the_retention_window()
    {
        // Every turn resends the whole transcript, so without a retention window a long
        // exploration keeps growing the request forever. Three 100 KB results against a 150 KB
        // window should leave only the most recent one in full.
        var harness = new SessionHarness { Budget = LlmBudget.Default with { ToolResultRetentionBytes = 150 * 1024 } };

        var bigTool = new LlmToolDefinition
        {
            Name = "big",
            Description = "Returns a large result.",
            ParametersSchemaJson = """{"type":"object","properties":{},"additionalProperties":false}""",
            Invoke = (_, _) => ValueTask.FromResult(LlmToolResult.Success(new string('x', 100 * 1024))),
        };

        harness.Provider
            .Calls(("big", new { }))
            .Calls(("big", new { }))
            .Calls(("big", new { }))
            .Says("done");

        await using var session = harness.Build();
        var result = await session.RunAsync(
            SessionHarness.Conversation([bigTool]),
            null,
            TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(LlmRunOutcome.Completed);

        var toolResults = harness.Provider.LastRequest.ToolResults;
        toolResults.Count.ShouldBe(3);
        toolResults.Count(text => text.Contains("pruned")).ShouldBe(2);
        toolResults[^1].ShouldNotContain("pruned");
        toolResults[^1].Length.ShouldBe(100 * 1024);
    }
}
