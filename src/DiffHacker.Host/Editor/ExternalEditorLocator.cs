using System.Diagnostics;

namespace DiffHacker.Host.Editor;

/// <summary>
/// Finds VS Code and Visual Studio, so the interface can offer a button per editor that is actually
/// there instead of a button that fails when pressed.
/// <para>
/// Everything here is a read: <c>PATH</c>, a handful of well-known install directories, and — on
/// Windows only — <c>vswhere.exe</c>, which Visual Studio ships precisely so that nobody has to guess
/// at install locations. Nothing is written and nothing is executed except <c>vswhere</c> itself.
/// </para>
/// <para>
/// Answers are cached for the life of the process. Installing an editor while DiffHacker is open and
/// expecting the button to appear is a worse trade than probing the filesystem on every hover.
/// </para>
/// </summary>
public sealed class ExternalEditorLocator
{
    /// <summary>
    /// One probe for the life of the process, started by whichever call needs it first and awaited by
    /// everyone after. A <see cref="Lazy{T}"/> over a task rather than a semaphore, so there is no
    /// disposable field on a type that is otherwise pure lookup.
    /// </summary>
    private readonly Lazy<Task<Installed>> _installed;

    public ExternalEditorLocator() =>
        _installed = new Lazy<Task<Installed>>(
            ProbeAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The executable to launch for one editor, or null when it is not installed. Null is a fact
    /// about the machine, not a failure.
    /// </summary>
    public async ValueTask<string?> FindAsync(ExternalEditor editor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installed = await _installed.Value.ConfigureAwait(false);

        return editor switch
        {
            ExternalEditor.VsCode => installed.VsCode,
            ExternalEditor.VisualStudio => installed.VisualStudio,
            _ => null,
        };
    }

    /// <summary>
    /// Deliberately takes no cancellation token: the result is cached for every later caller, and a
    /// probe abandoned half-way by whoever happened to ask first would be cached as "nothing is
    /// installed" for the rest of the session. It is one filesystem sweep and one short subprocess.
    /// </summary>
    private static async Task<Installed> ProbeAsync() => new(
        FindVsCode(),
        await FindVisualStudioAsync().ConfigureAwait(false));

    private sealed record Installed(string? VsCode, string? VisualStudio);

    /// <summary>
    /// <c>code</c> on <c>PATH</c> first, since that is what a user who set it up expects, then the
    /// default install locations for people who never did.
    /// </summary>
    private static string? FindVsCode()
    {
        // On Windows the launcher is a batch file: `code` alone resolves to nothing runnable without
        // a shell, and we never use one.
        var names = OperatingSystem.IsWindows() ? new[] { "code.cmd", "code.exe" } : ["code"];

        foreach (var name in names)
        {
            if (OnPath(name) is { } found)
            {
                return found;
            }
        }

        foreach (var candidate in VsCodeInstallCandidates())
        {
            if (SafeExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> VsCodeInstallCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            yield return Path.Combine(local, "Programs", "Microsoft VS Code", "bin", "code.cmd");
            yield return Path.Combine(programFiles, "Microsoft VS Code", "bin", "code.cmd");
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code";
            yield break;
        }

        yield return "/usr/bin/code";
        yield return "/usr/local/bin/code";
        yield return "/snap/bin/code";
    }

    /// <summary>
    /// Visual Studio through <c>vswhere</c>, which is installed at a fixed path with every edition
    /// since 2017 and reports the newest installation's <c>devenv.exe</c>.
    /// </summary>
    private static async ValueTask<string?> FindVisualStudioAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");

        if (!SafeExists(vswhere))
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(vswhere)
            {
                ArgumentList = { "-latest", "-prerelease", "-property", "productPath", "-nologo" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            });

            if (process is null)
            {
                return null;
            }

            // vswhere never reads stdin, but a child inheriting ours is the bug that made every MCP
            // call block for a full timeout, and the fix belongs everywhere a process is started.
            process.StandardInput.Close();

            // vswhere answers in milliseconds, but a hung one must not hold the first hover of the
            // diff panel forever.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var output = await process.StandardOutput.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            _ = await process.StandardError.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);

            var path = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            return path is { Length: > 0 } && SafeExists(path) ? path : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException
            or OperationCanceledException)
        {
            // A machine where vswhere exists and will not run is a machine without Visual Studio as
            // far as this is concerned.
            return null;
        }
    }

    private static string? OnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;

            try
            {
                candidate = Path.Combine(directory.Trim('"'), fileName);
            }
            catch (ArgumentException)
            {
                // A PATH entry with invalid characters in it. Someone else's problem.
                continue;
            }

            if (SafeExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="File.Exists"/> already swallows everything, but a path from <c>PATH</c> or from a
    /// known folder that is too long or malformed throws before it gets that far.
    /// </summary>
    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
