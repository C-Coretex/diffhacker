namespace DiffHacker.Host;

/// <summary>
/// The host's command line. Deliberately tiny — DiffHacker is a desktop application, and the
/// only switches that exist are the ones a developer or the end-to-end suite genuinely needs.
/// </summary>
public sealed record HostCommandLine
{
    /// <summary>Lower the log threshold to Debug.</summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Keep settings, the recent-repository list, the secret store and the log somewhere other
    /// than the per-user application data directory.
    /// <para>
    /// Needed because .NET resolves the real one through the Win32 known-folder API, which
    /// ignores <c>LOCALAPPDATA</c> — so on Windows there is no environment variable a test
    /// harness could redirect. Without this switch the end-to-end suite would write its
    /// throwaway providers and API keys into the developer's actual secret store.
    /// </para>
    /// </summary>
    public string? DataDirectory { get; init; }

    public static HostCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var result = new HostCommandLine();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--verbose":
                    result = result with { Verbose = true };
                    break;

                case "--data-dir" when i + 1 < args.Length:
                    result = result with { DataDirectory = args[++i] };
                    break;

                default:
                    // Unknown switches are ignored rather than fatal: the WebView host may be
                    // launched by an OS shell that appends its own arguments.
                    break;
            }
        }

        return result;
    }
}
