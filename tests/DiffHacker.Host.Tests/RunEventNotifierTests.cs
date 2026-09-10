using System.Text.Json;
using DiffHacker.Core.Llm;
using DiffHacker.Host.Rpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The host half of the live run's mechanical feed: an <see cref="LlmRunEvent"/> leaving as an
/// <c>analysis.toolCall</c> notification, with a host-stamped timestamp added at the boundary.
/// </summary>
public sealed class RunEventNotifierTests
{
    private sealed class CapturingNotifier : IRpcNotifier
    {
        public List<(string Method, object Payload)> Sent { get; } = [];

        public Task NotifyAsync(string method, object payload, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, payload));
            return Task.CompletedTask;
        }
    }

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private static JsonElement Serialise(object payload) =>
        JsonSerializer.SerializeToElement(payload, WireOptions);

    private static (CapturingNotifier Notifier, RunEventNotifier Sink) Build()
    {
        var notifier = new CapturingNotifier();
        return (notifier, new RunEventNotifier(notifier, NullLogger<RunEventNotifier>.Instance));
    }

    [Fact]
    public async Task Every_event_carries_a_host_stamped_timestamp()
    {
        var (notifier, sink) = Build();

        sink.Report(new LlmRunEvent { Kind = LlmRunEventKind.ToolCallStarted, Turn = 1, ToolName = "read_file" });
        await WaitForDelivery(notifier);

        var json = Serialise(notifier.Sent.Single().Payload);
        json.TryGetProperty("atUtc", out var atUtc).ShouldBeTrue();

        // DateTimeOffset, not DateTime: the wire value carries an explicit offset rather than a
        // trailing "Z", and comparing DateTime values ignores Kind entirely — it is the offset
        // form that compares by instant regardless of which offset the string happened to use.
        DateTimeOffset.Parse(atUtc.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind)
            .ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task An_assistant_message_carries_its_reasoning_and_response_text()
    {
        var (notifier, sink) = Build();

        sink.Report(new LlmRunEvent
        {
            Kind = LlmRunEventKind.AssistantMessage,
            Turn = 3,
            ReasoningText = "Checking whether the cache eviction path changed",
            ResponseText = "The eviction path is unchanged; only logging was added.",
        });
        await WaitForDelivery(notifier);

        var (method, payload) = notifier.Sent.ShouldHaveSingleItem();
        method.ShouldBe("analysis.toolCall");

        var json = Serialise(payload);
        json.GetProperty("kind").GetString().ShouldBe("assistant_message");
        json.GetProperty("reasoningText").GetString()
            .ShouldBe("Checking whether the cache eviction path changed");
        json.GetProperty("responseText").GetString()
            .ShouldBe("The eviction path is unchanged; only logging was added.");
    }

    [Fact]
    public async Task An_assistant_message_with_neither_field_omits_both_rather_than_sending_nulls()
    {
        var (notifier, sink) = Build();

        sink.Report(new LlmRunEvent { Kind = LlmRunEventKind.AssistantMessage, Turn = 1 });
        await WaitForDelivery(notifier);

        var json = Serialise(notifier.Sent.Single().Payload);
        json.TryGetProperty("reasoningText", out _).ShouldBeFalse();
        json.TryGetProperty("responseText", out _).ShouldBeFalse();
    }

    /// <summary>
    /// <see cref="RunEventNotifier.Report"/> fires the notification without awaiting it, so a test
    /// observing the result has to give the fire-and-forget task a turn to run.
    /// </summary>
    private static async Task WaitForDelivery(CapturingNotifier notifier)
    {
        for (var i = 0; i < 100 && notifier.Sent.Count == 0; i++)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }
}
