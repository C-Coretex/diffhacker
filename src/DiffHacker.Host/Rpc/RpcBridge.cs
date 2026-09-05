using System.Text.Json;
using System.Text.Json.Serialization;
using DiffHacker.Host.Shell;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Wires JSON-RPC 2.0 onto the shell's message channel and exposes the host's method surface.
/// </summary>
public sealed class RpcBridge : IAsyncDisposable
{
    private readonly JsonRpc _rpc;
    private readonly PhotinoRpcMessageHandler _handler;
    private readonly ILogger<RpcBridge> _logger;

    public RpcBridge(
        IAppShell shell,
        RpcNotifier notifier,
        IEnumerable<object> targets,
        ILogger<RpcBridge> logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(targets);

        _logger = logger;
        _handler = new PhotinoRpcMessageHandler(shell, new SystemTextJsonFormatter
        {
            JsonSerializerOptions = CreateSerializerOptions(),
        });

        _rpc = new JsonRpc(_handler);
        _rpc.Disconnected += OnDisconnected;

        foreach (var target in targets)
        {
            // Method names come from [JsonRpcMethod] attributes, so the wire names are
            // explicit rather than derived from C# naming.
            _rpc.AddLocalRpcTarget(target, new JsonRpcTargetOptions { AllowNonPublicInvocation = false });
            _logger.LogDebug("Registered RPC target {Target}", target.GetType().Name);
        }

        notifier.Attach(_rpc);
    }

    /// <summary>Begins processing inbound messages.</summary>
    public void Start()
    {
        _rpc.StartListening();
        _logger.LogInformation("JSON-RPC bridge listening");
    }

    /// <summary>Completes when the renderer disconnects or the bridge is disposed.</summary>
    public Task Completion => _rpc.Completion;

    public async ValueTask DisposeAsync()
    {
        _rpc.Disconnected -= OnDisconnected;
        _rpc.Dispose();
        await _handler.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Generated contract types carry explicit <c>[JsonPropertyName]</c> attributes, so no
    /// naming policy is applied here — the schema decides the wire names.
    /// </summary>
    internal static JsonSerializerOptions CreateSerializerOptions() => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private void OnDisconnected(object? sender, JsonRpcDisconnectedEventArgs e)
    {
        if (e.Exception is null)
        {
            _logger.LogInformation("JSON-RPC bridge disconnected: {Reason}", e.Reason);
            return;
        }

        _logger.LogWarning(e.Exception, "JSON-RPC bridge disconnected: {Reason}", e.Reason);
    }
}
