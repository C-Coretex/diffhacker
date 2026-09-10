using DiffHacker.Core.Llm;
using Microsoft.Extensions.AI;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// <see cref="LlmRunEventKind.AssistantMessage"/>: the model's own reasoning and reply text,
/// surfaced turn by turn alongside the existing tool-call and turn events.
/// <para>
/// The finishing turn is treated differently on purpose — see the call site in
/// <c>LlmSession.RunAsync</c> — so it gets its own test rather than being assumed to behave like
/// every other turn.
/// </para>
/// </summary>
public sealed class LlmSessionAssistantMessageTests
{
    private const string Good = """{"summary":"ok","confidence":4}""";

    [Fact]
    public async Task An_assistant_message_carries_the_reasoning_and_text_produced_alongside_a_tool_call()
    {
        var harness = new SessionHarness();
        harness.Provider.Responds(new ChatResponse(new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent("Checking whether the cache eviction path changed."),
            new TextContent("I'll look at the cache first."),
            new FunctionCallContent("call-1", "echo", new Dictionary<string, object?> { ["text"] = "x" }),
        ]))
        {
            Usage = FakeChatClient.Usage(20, 8),
            FinishReason = ChatFinishReason.ToolCalls,
        });
        harness.Provider.Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            harness.Progress,
            TestContext.Current.CancellationToken);

        var message = harness.Events.First(e => e.Kind == LlmRunEventKind.AssistantMessage);
        message.ReasoningText.ShouldBe("Checking whether the cache eviction path changed.");
        message.ResponseText.ShouldBe("I'll look at the cache first.");
    }

    [Fact]
    public async Task A_turn_with_neither_reasoning_nor_text_raises_no_assistant_message()
    {
        var harness = new SessionHarness();
        harness.Provider.Calls(("echo", new { text = "x" })).Says("done");

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation([SessionHarness.EchoTool()]),
            harness.Progress,
            TestContext.Current.CancellationToken);

        // "done" is the plain-text final answer here (no response format), so it does earn one
        // event; the pure tool-calling turn before it must not add a second, empty one.
        harness.Events.Count(e => e.Kind == LlmRunEventKind.AssistantMessage).ShouldBe(1);
        harness.Events.Single(e => e.Kind == LlmRunEventKind.AssistantMessage).ResponseText.ShouldBe("done");
    }

    [Fact]
    public async Task The_finishing_turns_answer_text_is_withheld_when_a_structured_result_is_expected()
    {
        // The answer document is the result screen's job to show. Surfacing it a second time
        // through the live log would be exactly the bulk-injection §0.2.9 rules out everywhere
        // else — a run can produce a document covering 1,500 files.
        var harness = new SessionHarness();
        harness.Provider.Says(Good);

        await using var session = harness.Build();
        await session.RunAsync(
            SessionHarness.Conversation(format: SessionHarness.AnswerFormat),
            harness.Progress,
            TestContext.Current.CancellationToken);

        harness.Events.ShouldNotContain(e => e.Kind == LlmRunEventKind.AssistantMessage);
    }
}
