namespace DiffHacker.Host.Shell;

/// <summary>
/// The native window, reduced to the four things the rest of the application needs from it:
/// serve assets, send a message, receive a message, and open or close.
/// <para>
/// CLAUDE.md §0.3: Photino types must not appear anywhere outside the implementation of this
/// interface. Everything above this line is testable without a window.
/// </para>
/// </summary>
public interface IAppShell : IDisposable
{
    /// <summary>Raised when the renderer posts a message to the host.</summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Registers an in-process handler for a custom URI scheme. Must be called before
    /// <see cref="Run"/>. There is deliberately no HTTP server and no localhost port.
    /// </summary>
    void RegisterAssetScheme(string scheme, Func<Uri, AssetResponse?> resolve);

    /// <summary>
    /// Posts a message to the renderer. Messages sent before the window exists are queued and
    /// delivered once it does.
    /// </summary>
    void SendMessage(string message);

    /// <summary>
    /// Opens the window at <paramref name="startUrl"/> and blocks until it closes. Must be
    /// called on the process main thread.
    /// </summary>
    void Run(Uri startUrl);

    /// <summary>Requests that the window close. Safe to call from any thread.</summary>
    void Close();
}
