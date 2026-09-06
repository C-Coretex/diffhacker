using System.Text.Json;
using DiffHacker.Contracts;
using DiffHacker.Host.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The host half of the bridge: a request in, a typed result out, notifications pushed back,
/// and failures carrying a contract error code.
/// <para>
/// The end-to-end suite covers these paths through the real window as well. This is still worth
/// keeping because it is where a bridge fault is *diagnosable* — a failure here names the frame
/// that went wrong, where the same fault in the window only shows a screen that never filled in.
/// </para>
/// </summary>
public sealed class RpcBridgeTests : IAsyncLifetime
{
    private readonly FakeAppShell _shell = new();
    private readonly RpcNotifier _notifier = new(NullLogger<RpcNotifier>.Instance);
    private readonly NotifyingTarget _notifying;
    private RpcBridge _bridge = null!;

    public RpcBridgeTests() => _notifying = new NotifyingTarget(_notifier);

    public ValueTask InitializeAsync()
    {
        _bridge = new RpcBridge(
            _shell,
            _notifier,
            [
                new HostRpcTarget(new HostRuntimeInfo(), NullLogger<HostRpcTarget>.Instance),
                _notifying,
            ],
            NullLogger<RpcBridge>.Instance);

        _bridge.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _bridge.DisposeAsync();
        _shell.Dispose();
    }

    [Fact]
    public async Task Ping_returns_the_host_identity_and_the_contract_version()
    {
        _shell.Receive("""{"jsonrpc":"2.0","id":1,"method":"host.ping","params":[]}""");

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var result = response.RootElement.GetProperty("result");

        response.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
        result.GetProperty("contractVersion").GetString().ShouldBe(ContractVersion.Current);
        result.GetProperty("platform").GetString().ShouldBeOneOf("windows", "macos", "linux");
    }

    [Fact]
    public async Task A_target_can_push_notifications_back_to_the_renderer()
    {
        // Nothing in the application pushes notifications yet — the demo target that used to was
        // scaffolding and has been removed. Iteration 5's `report_progress` and Iteration 7's
        // live analysis progress both depend on this channel, so it stays proven with a local
        // target rather than left untested until then.
        _shell.Receive("""{"jsonrpc":"2.0","id":3,"method":"test.notifyTwice","params":[]}""");

        // Three frames, and deliberately no assumption about their order: whether the response
        // precedes the notifications depends on whether the target awaits them or fires and
        // forgets, which is the target's business rather than the bridge's.
        var documents = new List<JsonDocument>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                documents.Add(JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken)));
            }

            var frames = documents.Select(document => document.RootElement).ToArray();

            var responses = frames.Where(frame => frame.TryGetProperty("id", out _)).ToArray();
            responses.ShouldHaveSingleItem().GetProperty("id").GetInt32().ShouldBe(3);

            var notifications = frames.Where(frame => !frame.TryGetProperty("id", out _)).ToArray();
            notifications.Length.ShouldBe(2);

            for (var step = 0; step < notifications.Length; step++)
            {
                notifications[step].GetProperty("method").GetString().ShouldBe("test/progress");

                // Notification parameters go out as a JSON object, unlike requests, which are
                // positional. A renderer that assumed an array would silently see nothing.
                notifications[step].GetProperty("params").GetProperty("step").GetInt32().ShouldBe(step);
            }
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_rejected_request_carries_a_stable_error_code_not_prose()
    {
        _shell.Receive("""{"jsonrpc":"2.0","id":9,"method":"test.refuse","params":[]}""");

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var error = response.RootElement.GetProperty("error");

        error.GetProperty("code").GetInt32().ShouldBe(RpcErrors.ApplicationErrorCode);
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("rpc_timeout");
        error.GetProperty("data").GetProperty("args").GetProperty("method").GetString().ShouldBe("test.refuse");
    }

    [Fact]
    public async Task An_unknown_method_is_reported_as_method_not_found()
    {
        _shell.Receive("""{"jsonrpc":"2.0","id":11,"method":"host.doesNotExist","params":[]}""");

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));

        // -32601 is the JSON-RPC 2.0 reserved code for an unknown method.
        response.RootElement.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32601);
    }

    [Fact]
    public async Task A_cancel_request_stops_work_the_renderer_has_given_up_on()
    {
        // Iteration 4, requirement 4. Before this, the renderer's only escape from a slow call
        // was its own timeout: it stopped waiting while the host kept working, so a `git diff`
        // over a cold tree kept grinding and an LLM run kept spending. StreamJsonRpc answers
        // `$/cancelRequest` itself; what needed proving is that the token really does reach the
        // method, and that the call comes back as cancelled rather than as a result.
        _shell.Receive("""{"jsonrpc":"2.0","id":13,"method":"test.waitForever","params":[]}""");

        await _notifying.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        _shell.Receive("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":13}}""");

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));

        response.RootElement.GetProperty("id").GetInt32().ShouldBe(13);
        response.RootElement.TryGetProperty("error", out var error).ShouldBeTrue(
            "a cancelled call must not come back as a result.");

        // -32800 is the JSON-RPC reserved code for a request cancelled at the client's request.
        error.GetProperty("code").GetInt32().ShouldBe(-32800);

        _notifying.WasCancelled.ShouldBeTrue("the token has to reach the method, not just the dispatcher.");
    }

    /// <summary>
    /// A target that exists only for these tests: one method that pushes notifications, one that
    /// refuses, and one that waits to be cancelled — so the bridge's outbound shapes are all
    /// exercised without the product carrying an RPC surface for the benefit of a test.
    /// </summary>
    private sealed class NotifyingTarget(IRpcNotifier notifier)
    {
        /// <summary>Completes once <c>test.waitForever</c> is genuinely in flight.</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        [JsonRpcMethod("test.waitForever")]
        public async Task WaitForeverAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }

        [JsonRpcMethod("test.notifyTwice")]
        public async Task NotifyTwiceAsync(CancellationToken cancellationToken)
        {
            for (var step = 0; step < 2; step++)
            {
                await notifier
                    .NotifyAsync("test/progress", new { step }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        [JsonRpcMethod("test.refuse")]
        public static void Refuse() =>
            throw RpcErrors.Failure(
                "rpc_timeout",
                "a developer-facing message that must never reach the interface",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "test.refuse" });
    }
}
