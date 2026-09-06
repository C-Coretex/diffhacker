using System.Text.Json;
using DiffHacker.Core.Tools;
using DiffHacker.Host.Rpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The host half of Iteration 5's <c>report_progress</c> pipe: a toolbox progress report leaving
/// as an <c>analysis.progress</c> notification.
/// <para>
/// The renderer half is proven by <c>methods.test.ts</c> against the real RPC client. Neither
/// side is driven end-to-end through a live window yet, because nothing in the application starts
/// an analysis until Iteration 7 — that is the e2e test this iteration hands over rather than the
/// one it writes.
/// </para>
/// </summary>
public sealed class ToolProgressNotifierTests
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

    /// <summary>Matches the bridge's own serializer options, so this sees what the renderer sees.</summary>
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private static JsonElement Serialise(object payload) =>
        JsonSerializer.SerializeToElement(payload, WireOptions);

    [Fact]
    public async Task A_progress_report_leaves_as_an_analysis_progress_notification()
    {
        var notifier = new CapturingNotifier();
        var sink = new ToolProgressNotifier(notifier);

        await sink.ReportAsync(
            new ToolProgressReport(1, "reading the authentication changes", "exploring", DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var (method, payload) = notifier.Sent.ShouldHaveSingleItem();
        method.ShouldBe("analysis.progress");

        var json = Serialise(payload);
        json.GetProperty("sequence").GetInt32().ShouldBe(1);
        json.GetProperty("message").GetString().ShouldBe("reading the authentication changes");
        json.GetProperty("phase").GetString().ShouldBe("exploring");
        json.TryGetProperty("atUtc", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("exploring")]
    [InlineData("analysing")]
    [InlineData("grouping")]
    [InlineData("explaining")]
    [InlineData("finishing")]
    public async Task Every_phase_the_model_is_offered_survives_the_wire(string phase)
    {
        var notifier = new CapturingNotifier();
        var sink = new ToolProgressNotifier(notifier);

        await sink.ReportAsync(
            new ToolProgressReport(1, "working", phase, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Serialise(notifier.Sent.Single().Payload).GetProperty("phase").GetString().ShouldBe(phase);
    }

    [Fact]
    public async Task A_phase_the_model_invented_is_dropped_rather_than_sent_as_an_unknown_key()
    {
        var notifier = new CapturingNotifier();
        var sink = new ToolProgressNotifier(notifier);

        await sink.ReportAsync(
            new ToolProgressReport(1, "working", "vibing", DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        // The renderer translates the phase through its catalogue. A value it has no string for
        // would reach a reader as a raw identifier, so no phase is better than a wrong one.
        var json = Serialise(notifier.Sent.Single().Payload);
        json.TryGetProperty("phase", out var value).ShouldBeFalse($"got {value}");
        json.GetProperty("message").GetString().ShouldBe("working");
    }

    [Fact]
    public async Task The_message_crosses_untranslated_because_it_is_run_data_not_UI_copy()
    {
        var notifier = new CapturingNotifier();
        var sink = new ToolProgressNotifier(notifier);

        const string written = "Tracing how the session store reaches the login handler";

        await sink.ReportAsync(
            new ToolProgressReport(7, written, null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Serialise(notifier.Sent.Single().Payload).GetProperty("message").GetString().ShouldBe(written);
    }
}
