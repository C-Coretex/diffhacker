using System.Buffers;
using System.Text;
using System.Threading.Channels;
using DiffHacker.Host.Shell;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Carries JSON-RPC 2.0 over the Photino message channel.
/// <para>
/// StreamJsonRpc normally sits on a duplex stream and frames messages itself. The shell's
/// channel is already message-oriented — one string in, one string out — so this handler owns
/// the framing instead: exactly one Photino message is exactly one JSON-RPC message, and no
/// <c>Content-Length</c> header is needed.
/// </para>
/// </summary>
public sealed class PhotinoRpcMessageHandler : MessageHandlerBase
{
    private readonly IAppShell _shell;
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <param name="shell">Transport. Subscribed for the lifetime of this handler.</param>
    /// <param name="formatter">
    /// Must not be shared with another handler: StreamJsonRpc formatters make thread-safety
    /// assumptions that only hold for a single owner.
    /// </param>
    public PhotinoRpcMessageHandler(IAppShell shell, IJsonRpcMessageTextFormatter formatter)
        : base(formatter)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        _shell.MessageReceived += OnMessageReceived;
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    private IJsonRpcMessageTextFormatter TextFormatter => (IJsonRpcMessageTextFormatter)Formatter;

    protected override async ValueTask<JsonRpcMessage?> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var text = await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes(text);
        return TextFormatter.Deserialize(new ReadOnlySequence<byte>(bytes), Encoding.UTF8);
    }

    protected override ValueTask WriteCoreAsync(JsonRpcMessage content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var buffer = new ArrayBufferWriter<byte>();
        TextFormatter.Serialize(buffer, content);
        _shell.SendMessage(Encoding.UTF8.GetString(buffer.WrittenSpan));

        return default;
    }

    protected override ValueTask FlushAsync(CancellationToken cancellationToken) => default;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shell.MessageReceived -= OnMessageReceived;
            _inbound.Writer.TryComplete();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Runs on the WebView UI thread, so it must not block: hand off to the channel and return.
    /// </summary>
    private void OnMessageReceived(object? sender, string message) => _inbound.Writer.TryWrite(message);
}
