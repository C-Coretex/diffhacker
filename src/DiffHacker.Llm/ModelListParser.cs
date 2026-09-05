using System.Text.Json;

namespace DiffHacker.Llm;

/// <summary>
/// Pulls model identifiers out of a provider's listing response.
/// <para>
/// Two shapes cover every provider DiffHacker supports: OpenAI-style
/// <c>{ "data": [ { "id": ... } ] }</c>, which Anthropic also uses, and Google's
/// <c>{ "models": [ { "name": "models/..." } ] }</c>. Anything else yields an empty list rather
/// than an error — a provider that does not list models is not a broken key.
/// </para>
/// </summary>
internal static class ModelListParser
{
    public static IReadOnlyList<string> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (document.RootElement.TryGetProperty("data", out var data))
            {
                return Collect(data, "id");
            }

            if (document.RootElement.TryGetProperty("models", out var models))
            {
                return Collect(models, "name");
            }

            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> Collect(JsonElement array, string property)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(property, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = value.GetString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                models.Add(Normalise(id));
            }
        }

        return models;
    }

    /// <summary>
    /// Google returns fully qualified names like <c>models/gemini-2.5-pro</c>, but users type
    /// the bare identifier. Strip the prefix so model verification compares like with like.
    /// </summary>
    internal static string Normalise(string id) =>
        id.StartsWith("models/", StringComparison.Ordinal) ? id["models/".Length..] : id;
}
