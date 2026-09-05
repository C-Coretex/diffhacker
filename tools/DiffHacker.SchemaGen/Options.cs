namespace DiffHacker.SchemaGen;

/// <summary>
/// Command line arguments for the contract generator.
/// </summary>
internal sealed record Options
{
    public required string SchemaDirectory { get; init; }

    public required string CSharpOutputDirectory { get; init; }

    public required string TypeScriptOutputDirectory { get; init; }

    public required string Namespace { get; init; }

    public string? StampFile { get; init; }

    /// <summary>
    /// Parses the command line. Returns <see langword="null"/> and writes to
    /// <paramref name="error"/> when the arguments are not usable.
    /// </summary>
    public static Options? Parse(string[] args, out string? error)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{key}'.";
                return null;
            }

            if (i + 1 >= args.Length)
            {
                error = $"Argument '{key}' is missing a value.";
                return null;
            }

            values[key] = args[++i];
        }

        if (!values.TryGetValue("--schema", out var schema) ||
            !values.TryGetValue("--csharp", out var csharp) ||
            !values.TryGetValue("--typescript", out var typescript))
        {
            error = "Required arguments: --schema <dir> --csharp <dir> --typescript <dir> " +
                    "[--namespace <ns>] [--stamp <file>].";
            return null;
        }

        error = null;
        return new Options
        {
            SchemaDirectory = Path.GetFullPath(schema),
            CSharpOutputDirectory = Path.GetFullPath(csharp),
            TypeScriptOutputDirectory = Path.GetFullPath(typescript),
            Namespace = values.TryGetValue("--namespace", out var ns) ? ns : "DiffHacker.Contracts",
            StampFile = values.TryGetValue("--stamp", out var stamp) ? Path.GetFullPath(stamp) : null,
        };
    }
}
