using System.Text.Json;
using DiffHacker.Contracts;
using DiffHacker.Host.Rpc;
using DiffHacker.Host.SelfTest;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// End-to-end coverage of the host half of the bridge: a request in, a typed result out,
/// notifications pushed back, and failures carrying a contract error code.
/// </summary>
public sealed class RpcBridgeTests : IAsyncLifetime
{
    private readonly FakeAppShell _shell = new();
    private readonly RpcNotifier _notifier = new(NullLogger<RpcNotifier>.Instance);
    private readonly SelfTestCoordinator _selfTest = new(NullLogger<SelfTestCoordinator>.Instance);
    private RpcBridge _bridge = null!;

    public ValueTask InitializeAsync()
    {
        var runtime = new HostRuntimeInfo { SelfTest = true };

        _bridge = new RpcBridge(
            _shell,
            _notifier,
            [
                new HostRpcTarget(runtime, _selfTest, NullLogger<HostRpcTarget>.Instance),
                new DemoRpcTarget(_notifier, NullLogger<DemoRpcTarget>.Instance),
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
        result.GetProperty("selfTest").GetBoolean().ShouldBeTrue();
        result.GetProperty("platform").GetString().ShouldBeOneOf("windows", "macos", "linux");
    }

    [Fact]
    public async Task StartCountdown_answers_then_streams_progress_notifications()
    {
        _shell.Receive(
            """{"jsonrpc":"2.0","id":7,"method":"demo.startCountdown","params":[{"steps":3,"delayMilliseconds":0}]}""");

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var operationId = response.RootElement.GetProperty("result").GetProperty("operationId").GetString();

        response.RootElement.GetProperty("result").GetProperty("totalSteps").GetInt32().ShouldBe(3);
        operationId.ShouldNotBeNullOrWhiteSpace();

        for (var expectedStep = 0; expectedStep < 3; expectedStep++)
        {
            using var notification = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
            var root = notification.RootElement;

            root.TryGetProperty("id", out _).ShouldBeFalse("a notification must not carry an id");
            root.GetProperty("method").GetString().ShouldBe("demo/progress");

            var parameters = root.GetProperty("params");
            parameters.GetProperty("operationId").GetString().ShouldBe(operationId);
            parameters.GetProperty("step").GetInt32().ShouldBe(expectedStep);
            parameters.GetProperty("totalSteps").GetInt32().ShouldBe(3);
            parameters.GetProperty("completed").GetBoolean().ShouldBe(expectedStep == 2);
            // A resource key, never a sentence: the renderer owns the wording.
            parameters.GetProperty("message").GetString().ShouldBe("demo.step");
        }
    }

    [Fact]
    public async Task A_rejected_request_carries_a_stable_error_code_not_prose()
    {
        _shell.Receive(
            """{"jsonrpc":"2.0","id":9,"method":"demo.startCountdown","params":[{"steps":0}]}""");

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var error = response.RootElement.GetProperty("error");

        error.GetProperty("code").GetInt32().ShouldBe(RpcErrors.ApplicationErrorCode);
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("demo_steps_out_of_range");
        error.GetProperty("data").GetProperty("args").GetProperty("steps").GetString().ShouldBe("0");
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
    public async Task ReportSelfTest_hands_the_verdict_to_the_coordinator()
    {
        _shell.Receive(
            """
            {"jsonrpc":"2.0","id":13,"method":"host.reportSelfTest","params":[
              {"succeeded":true,"checks":[{"name":"rpc_round_trip","passed":true}]}]}
            """);

        await _shell.NextSentAsync(TestContext.Current.CancellationToken);

        var result = await _selfTest.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Succeeded.ShouldBeTrue();
        result.Checks.ShouldHaveSingleItem().Name.ShouldBe("rpc_round_trip");
    }
}
