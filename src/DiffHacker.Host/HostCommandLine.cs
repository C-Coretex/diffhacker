using System.Globalization;

namespace DiffHacker.Host;

/// <summary>
/// The host's command line. Deliberately tiny — DiffHacker is a desktop application, and the
/// only switches that exist are the ones CI needs.
/// </summary>
public sealed record HostCommandLine
{
    private const int DefaultSelfTestTimeoutSeconds = 60;

    /// <summary>
    /// Run the renderer's bridge verification, write the verdict to
    /// <see cref="SelfTestOutputPath"/>, and exit with 0 or 1 instead of waiting for the user
    /// to close the window.
    /// </summary>
    public bool SelfTest { get; init; }

    public TimeSpan SelfTestTimeout { get; init; } = TimeSpan.FromSeconds(DefaultSelfTestTimeoutSeconds);

    public string SelfTestOutputPath { get; init; } = "selftest-result.json";

    /// <summary>Lower the log threshold to Debug.</summary>
    public bool Verbose { get; init; }

    public static HostCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var result = new HostCommandLine();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--self-test":
                    result = result with { SelfTest = true };
                    break;

                case "--verbose":
                    result = result with { Verbose = true };
                    break;

                case "--timeout" when i + 1 < args.Length &&
                                      int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var seconds):
                    result = result with { SelfTestTimeout = TimeSpan.FromSeconds(seconds) };
                    i++;
                    break;

                case "--out" when i + 1 < args.Length:
                    result = result with { SelfTestOutputPath = args[++i] };
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
