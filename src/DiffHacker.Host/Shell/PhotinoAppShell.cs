using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace DiffHacker.Host.Shell;

/// <summary>
/// The one and only place in DiffHacker that knows the windowing library exists.
/// <para>
/// Enforced by <c>DiffHacker.Architecture.Tests</c>: no other file under <c>/src</c> may
/// reference the <c>Photino</c> namespace.
/// </para>
/// </summary>
public sealed class PhotinoAppShell(ILogger<PhotinoAppShell> logger, WindowSettings settings) : IAppShell
{
    private readonly PhotinoApplication _application = new();
    private readonly PhotinoWindow _window = new();
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly Lock _sendGate = new();

    private bool _windowReady;
    private bool _disposed;

    public event EventHandler<string>? MessageReceived;

    public void RegisterAssetScheme(string scheme, Func<Uri, AssetResponse?> resolve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(resolve);

        logger.LogInformation("Registering custom scheme handler for {Scheme}", scheme);

        _window.RegisterCustomSchemeHandler(scheme, (PhotinoWindow _, string _, string url, out string? contentType) =>
        {
            contentType = null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                logger.LogWarning("Custom scheme handler received an unparseable URL: {Url}", url);
                return null;
            }

            try
            {
                var response = resolve(uri);
                if (response is null)
                {
                    return null;
                }

                contentType = response.ContentType;
                return response.Content;
            }
#pragma warning disable CA1031 // An escaping exception would cross the native boundary and kill the process.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Asset handler threw for {Url}", url);
                return null;
            }
        });
    }

    public void SendMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // SendWebMessage throws before the native window exists, so anything produced during
        // startup is held until the window is created.
        lock (_sendGate)
        {
            if (!_windowReady)
            {
                _pending.Enqueue(message);
                return;
            }
        }

        _window.SendWebMessage(message);
    }

    public void Run(Uri startUrl)
    {
        ArgumentNullException.ThrowIfNull(startUrl);
        ObjectDisposedException.ThrowIf(_disposed, this);

        logger.LogInformation("Opening window at {StartUrl}", startUrl);

        _window
            .SetTitle(settings.Title)
            .SetUseOsDefaultSize(false)
            .SetSize(settings.Width, settings.Height)
            .SetUseOsDefaultLocation(false)
            .Center()
            .SetContextMenuEnabled(settings.DevToolsEnabled)
            .SetDevToolsEnabled(settings.DevToolsEnabled)
            .RegisterCreatedHandler((_, _) => OnWindowCreated())
            .RegisterWebMessageReceivedHandler((_, e) => MessageReceived?.Invoke(this, e.Message))
            .Load(startUrl);

        // Owns the native message loop; returns once the window closes.
        _application.Run(_window);

        logger.LogInformation("Window closed");
    }

    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        // Marshalled onto the UI thread by the dispatcher.
        _window.Invoke(_window.Close);
    }

    public Task<string?> ShowFolderPickerAsync(string title, string? initialDirectory, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sendGate)
        {
            if (!_windowReady)
            {
                // The dialog is owned by the native window; there is nothing to parent it to
                // yet. This only happens if the renderer asks before it has finished loading.
                return Task.FromResult<string?>(null);
            }
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The dialog is modal on the UI thread, and RPC handlers run on a background thread,
        // so it has to be marshalled across. Invoke is Photino's dispatcher.
        _window.Invoke(() =>
        {
            try
            {
                var chosen = _window.ShowOpenFolder(title, initialDirectory, multiSelect: false);
                completion.TrySetResult(chosen is { Length: > 0 } ? chosen[0] : null);
            }
#pragma warning disable CA1031 // Anything escaping here would cross the native boundary and kill the process.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "The native folder picker failed");
                completion.TrySetException(ex);
            }
        });

        return completion.Task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        MessageReceived = null;
    }

    private void OnWindowCreated()
    {
        lock (_sendGate)
        {
            _windowReady = true;
        }

        var flushed = 0;
        while (_pending.TryDequeue(out var message))
        {
            _window.SendWebMessage(message);
            flushed++;
        }

        logger.LogDebug("Window created; flushed {Count} queued message(s)", flushed);
    }
}
