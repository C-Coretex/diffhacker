namespace DiffHacker.Host.Rpc;

/// <summary>
/// Sends server-to-client JSON-RPC notifications. Separate from <see cref="RpcBridge"/> so
/// that RPC targets can push progress without depending on the bridge that constructs them.
/// <para>
/// Nothing in the application pushes notifications yet — the demo target that used to was
/// scaffolding and has been removed. The plumbing stays because Iteration 5's
/// <c>report_progress</c> and Iteration 7's live analysis progress are what it exists for, and
/// <c>RpcBridgeTests</c> keeps it proven in the meantime.
/// </para>
/// </summary>
public interface IRpcNotifier
{
    Task NotifyAsync(string method, object payload, CancellationToken cancellationToken = default);
}
