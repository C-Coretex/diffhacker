using DiffHacker.Core.Llm;
using DiffHacker.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// The scaffolding every session test needs: a profile, a budget, a fake provider, and a
/// session wired to them with the clock taken out.
/// <para>
/// Retry delays are handed a no-op so the backoff curve is asserted from the recorded delays
/// rather than by waiting through them. A retry test that sleeps is a retry test that fails on
/// somebody else's machine.
/// </para>
/// </summary>
internal sealed class SessionHarness
{
    public FakeChatClient Provider { get; } = new();

    public List<TimeSpan> Delays { get; } = [];

    public List<LlmRunEvent> Events { get; } = [];

    public LlmBudget Budget { get; set; } = LlmBudget.Default;

    public LlmProviderProfile Profile { get; set; } = ProfileFor(LlmProviderType.OpenAi);

    public ITokenPricing Pricing { get; set; } = new StubPricing();

    public LlmSession Build() => new(
        Provider,
        new HttpClient(),
        Profile,
        Budget,
        Pricing,
        NullLogger<LlmSession>.Instance,
        jitter: () => 0.5,
        delay: (duration, _) =>
        {
            Delays.Add(duration);
            return Task.CompletedTask;
        });

    public IProgress<LlmRunEvent> Progress => new Recorder(Events);

    public static LlmProviderProfile ProfileFor(
        LlmProviderType type,
        string model = "gpt-4o",
        string? baseUrl = null) => new()
        {
            Id = "p1",
            ProviderType = type,
            DisplayName = "Test",
            Model = model,
            BaseUrl = baseUrl,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    /// <summary>An echo tool, the same one the live conformance suite uses.</summary>
    public static LlmToolDefinition EchoTool(Action<string>? onCall = null) => new()
    {
        Name = "echo",
        Description = "Repeats back the text it is given.",
        ParametersSchemaJson =
            """
            {"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false}
            """,
        Invoke = (arguments, _) =>
        {
            onCall?.Invoke(arguments);
            return ValueTask.FromResult(LlmToolResult.Success(arguments));
        },
    };

    public static LlmToolDefinition ThrowingTool(string name = "broken") => new()
    {
        Name = name,
        Description = "Always throws.",
        ParametersSchemaJson = """{"type":"object","properties":{},"additionalProperties":false}""",
        Invoke = (_, _) => throw new InvalidOperationException("the tool is broken"),
    };

    /// <summary>A small schema, standing in for Iteration 7's graph document.</summary>
    public static LlmResponseFormat AnswerFormat { get; } = new()
    {
        SchemaName = "answer",
        SchemaJson =
            """
            {
              "type": "object",
              "properties": {
                "summary": { "type": "string" },
                "confidence": { "type": "integer" }
              },
              "required": ["summary", "confidence"],
              "additionalProperties": false
            }
            """,
    };

    public static LlmConversation Conversation(
        IEnumerable<LlmToolDefinition>? tools = null,
        LlmResponseFormat? format = null) => new()
        {
            SystemPrompt = "You are reviewing a change.",
            UserMessage = "Describe it.",
            Tools = [.. tools ?? []],
            ResponseFormat = format,
        };

    private sealed class Recorder(List<LlmRunEvent> events) : IProgress<LlmRunEvent>
    {
        public void Report(LlmRunEvent value) => events.Add(value);
    }

    /// <summary>Prices everything at a round number so cost assertions read as arithmetic.</summary>
    private sealed class StubPricing : ITokenPricing
    {
        public DateOnly TableAsOf => new(2026, 1, 1);

        public bool TryGetRate(LlmProviderType providerType, string model, out LlmModelRate rate)
        {
            rate = new LlmModelRate { InputPerMillion = 1_000m, OutputPerMillion = 2_000m };
            return model != "unpriced-model";
        }
    }
}
