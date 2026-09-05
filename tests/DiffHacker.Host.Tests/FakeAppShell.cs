using System.Collections.Concurrent;
using DiffHacker.Host.Shell;

namespace DiffHacker.Host.Tests;

/// <summary>
/// A shell with no window.
/// <para>
/// This is the point of <see cref="IAppShell"/>: the whole bridge — correlation, error
/// mapping, notification streaming — is exercised headlessly in CI on every platform, with no
/// WebView, no display server and no timing luck involved.
/// </para>
/// </summary>
internal sealed class FakeAppShell : IAppShell
{
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly SemaphoreSlim _sentSignal = new(0);

    public event EventHandler<string>? MessageReceived;

    public List<(string Scheme, Func<Uri, AssetResponse?> Resolve)> RegisteredSchemes { get; } = [];

    public void RegisterAssetScheme(string scheme, Func<Uri, AssetResponse?> resolve) =>
        RegisteredSchemes.Add((scheme, resolve));

    public void SendMessage(string message)
    {
        _sent.Enqueue(message);
        _sentSignal.Release();
    }

    public void Run(Uri startUrl) => throw new NotSupportedException("The fake shell has no message loop.");

    public void Close()
    {
    }

    public void Dispose() => _sentSignal.Dispose();

    /// <summary>Simulates the renderer posting a message to the host.</summary>
    public void Receive(string message) => MessageReceived?.Invoke(this, message);

    /// <summary>Waits for the next message the host sends to the renderer.</summary>
    public async Task<string> NextSentAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await _sentSignal.WaitAsync(timeout.Token).ConfigureAwait(false);
        return _sent.TryDequeue(out var message)
            ? message
            : throw new InvalidOperationException("Signalled but no message was queued.");
    }
}
