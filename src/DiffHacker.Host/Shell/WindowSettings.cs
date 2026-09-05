namespace DiffHacker.Host.Shell;

/// <summary>
/// Window geometry and chrome. Not user settings — those arrive in Iteration 2.
/// </summary>
public sealed record WindowSettings
{
    public string Title { get; init; } = "DiffHacker";

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 860;

    /// <summary>
    /// Whether the WebView developer tools are reachable. Enabled in Debug so the CSP can be
    /// verified from the console; disabled in Release.
    /// </summary>
    public bool DevToolsEnabled { get; init; }
}
