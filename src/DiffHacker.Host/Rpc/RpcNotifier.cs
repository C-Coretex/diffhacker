using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Notifier backed by the live <see cref="JsonRpc"/> connection, which is attached by
/// <see cref="RpcBridge"/> once the connection exists.
/// </summary>
public sealed class RpcNotifier(ILogger<RpcNotifier> logger) : IRpcNotifier
{
    private JsonRpc? _rpc;

    internal void Attach(JsonRpc rpc) => _rpc = rpc;

    public async Task NotifyAsync(string method, object payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(payload);

        var rpc = _rpc;
        if (rpc is null)
        {
            logger.LogWarning("Dropped notification {Method}: the RPC connection is not open", method);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await rpc.NotifyWithParameterObjectAsync(method, payload).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The window closed mid-operation. Nothing to report.
            logger.LogDebug("Dropped notification {Method}: the connection closed", method);
        }
        catch (ConnectionLostException)
        {
            logger.LogDebug("Dropped notification {Method}: the connection was lost", method);
        }
    }
}
