using System.Collections.Frozen;
using System.Reflection;

namespace DiffHacker.Contracts;

/// <summary>
/// The JSON Schema documents from <c>/schema</c>, readable at run time.
/// <para>
/// Everywhere else in the application a schema is a build-time input: the generator turns it
/// into a C# record and a TypeScript interface, and nothing needs the original text. From
/// Iteration 4 that changes. The LLM is asked to answer in a shape defined by one of these
/// documents, and the answer is validated against it before being believed — neither of which
/// a generated record can do.
/// </para>
/// <para>
/// Names here are the schema's file name without <c>.schema.json</c>, so
/// <c>changeset-result.schema.json</c> is <c>"changeset-result"</c>. They are not the generated
/// type names, because the file is what is embedded.
/// </para>
/// </summary>
public static class ContractSchemas
{
    private const string Prefix = "DiffHacker.Contracts.Schemas.";
    private const string Suffix = ".schema.json";

    private static readonly FrozenDictionary<string, string> ResourceNames = BuildIndex();

    /// <summary>Every schema name available, ordered. Useful in tests and diagnostics.</summary>
    public static IReadOnlyCollection<string> Names { get; } =
        [.. ResourceNames.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// The raw JSON Schema text for <paramref name="name"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// No schema of that name is embedded. This is a build-time mistake — a typo or a schema
    /// that was renamed — so it fails loudly rather than returning null.
    /// </exception>
    public static string Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!ResourceNames.TryGetValue(name, out var resourceName))
        {
            throw new ArgumentException(
                $"There is no schema named '{name}'. Available: {string.Join(", ", Names)}.",
                nameof(name));
        }

        using var stream = typeof(ContractSchemas).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' disappeared.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Whether a schema of that name is embedded.</summary>
    public static bool Contains(string name) =>
        !string.IsNullOrWhiteSpace(name) && ResourceNames.ContainsKey(name);

    private static FrozenDictionary<string, string> BuildIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resourceName in typeof(ContractSchemas).Assembly.GetManifestResourceNames())
        {
            if (resourceName.StartsWith(Prefix, StringComparison.Ordinal)
                && resourceName.EndsWith(Suffix, StringComparison.Ordinal))
            {
                var name = resourceName[Prefix.Length..^Suffix.Length];
                index[name] = resourceName;
            }
        }

        return index.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
