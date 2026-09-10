namespace DiffHacker.Host.Editor;

/// <summary>Which external editor a request means.</summary>
public enum ExternalEditor
{
    /// <summary>VS Code, through its <c>code</c> command line.</summary>
    VsCode,

    /// <summary>Visual Studio, through <c>devenv.exe</c>. Windows only.</summary>
    VisualStudio,

    /// <summary>Whatever the user configured.</summary>
    Custom,
}

/// <summary>Failure codes the renderer resolves through its own catalogue.</summary>
public static class EditorFailures
{
    /// <summary>The editor is not installed, or the configured executable does not exist.</summary>
    public const string NotFound = "editor_not_found";

    /// <summary>It was found and starting it still failed.</summary>
    public const string LaunchFailed = "editor_launch_failed";

    /// <summary>A custom editor was asked for and no custom command is configured.</summary>
    public const string NotConfigured = "editor_not_configured";

    /// <summary>Every code above, for the test that asserts the taxonomy and the catalogue agree.</summary>
    public static IReadOnlyList<string> All { get; } = [NotFound, LaunchFailed, NotConfigured];
}

/// <summary>
/// Where a launch failed, as a typed answer rather than an exception carrying a message the interface
/// would have to read.
/// </summary>
public sealed class ExternalEditorException(string failure, string message)
    : Exception(message)
{
    /// <summary>One of <see cref="EditorFailures"/>.</summary>
    public string Failure { get; } = failure;
}
