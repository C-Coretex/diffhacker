namespace DiffHacker.Host.Rpc;

/// <summary>
/// Sends server-to-client JSON-RPC notifications. Separate from <see cref="RpcBridge"/> so
/// that RPC targets can push progress without depending on the bridge that constructs them.
/// </summary>
public interface IRpcNotifier
{
    /// <summary>Notification method for streamed operation progress.</summary>
    public const string ProgressMethod = "demo/progress";

    Task NotifyAsync(string method, object payload, CancellationToken cancellationToken = default);
}
