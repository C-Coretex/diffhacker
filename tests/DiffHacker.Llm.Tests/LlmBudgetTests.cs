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
    public void The_defaults_are_sized_for_a_large_changeset()
    {
        // These are user-visible behaviour, so the numbers are pinned rather than assumed.
        var budget = LlmBudget.Default;

        budget.MaxToolCalls.ShouldBe(500);
        budget.MaxTurns.ShouldBe(300);
        budget.MaxTotalTokens.ShouldBe(2_000_000);
        budget.MaxRetryAttempts.ShouldBe(5);
        budget.MaxConsecutiveToolFailures.ShouldBe(3);
        budget.RequestTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        budget.MaxCostUsd.ShouldBeNull(
            "a mid-run cost kill wastes everything already spent; Iteration 13's pre-run estimate is the place to prevent an expensive run.");
    }
}
