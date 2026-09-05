using System.Text.Json;

namespace DiffHacker.SchemaGen;

/// <summary>
/// Reads <c>/schema/contract-version.json</c>, the canonical version of the whole schema set.
/// </summary>
internal static class ContractVersionFile
{
    public const string FileName = "contract-version.json";

    public static string Read(string schemaDirectory)
    {
        var path = Path.Combine(schemaDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new SchemaGenException($"Contract version file not found: {path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.String)
        {
            throw new SchemaGenException($"{FileName} must contain a string 'version' property.");
        }

        var value = version.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SchemaGenException($"{FileName} contains an empty 'version'.");
        }

        return value;
    }
}
