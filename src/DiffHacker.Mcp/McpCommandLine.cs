namespace DiffHacker.Mcp;

/// <summary>
/// The server's arguments.
/// <para>
/// Hand-rolled, matching <c>HostCommandLine</c>. It differs from that one in being strict:
/// the host tolerates unknown switches because an OS shell may append its own, but this is
/// launched by an MCP client from a configuration file a person wrote, and silently ignoring a
/// misspelled switch there would present as "the server does not work" with nothing to go on.
/// </para>
/// </summary>
public sealed record McpCommandLine
{
    /// <summary>The repository to serve. Required.</summary>
    public string? Repository { get; init; }

    /// <summary>An optional file to log to, in addition to stderr.</summary>
    public string? LogFile { get; init; }

    public bool Verbose { get; init; }

    public bool Help { get; init; }

    /// <summary>What went wrong, when the arguments do not make a runnable server.</summary>
    public string? Error { get; init; }

    public const string Usage = """
        diffhacker-mcp — DiffHacker's repository toolbox, served over MCP on stdio.

          --repository <path>   The git repository to serve. Required.
          --log-file <path>     Also write the log to this file.
          --verbose             Log every tool call and its result size.
          --help                Show this.

        Serves ten read-only tools for exploring a repository and the change in its working tree.
        It never writes to the repository and never reaches the network.
        """;

    public static McpCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var result = new McpCommandLine();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "-?":
                    return result with { Help = true };

                case "--repository" or "--repo" when i + 1 < args.Length:
                    result = result with { Repository = args[++i] };
                    break;

                case "--log-file" when i + 1 < args.Length:
                    result = result with { LogFile = args[++i] };
                    break;

                case "--verbose":
                    result = result with { Verbose = true };
                    break;

                default:
                    return result with { Error = $"Unrecognised argument '{args[i]}'." };
            }
        }

        if (string.IsNullOrWhiteSpace(result.Repository))
        {
            return result with { Error = "--repository is required." };
        }

        return result;
    }
}
