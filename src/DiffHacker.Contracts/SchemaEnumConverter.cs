using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiffHacker.Contracts;

/// <summary>
/// Serialises an enum using exactly the strings its JSON Schema declares.
/// <para>
/// The generator emits <c>[EnumMember(Value = "windows")]</c> for each schema value, but
/// <see cref="JsonStringEnumConverter{TEnum}"/> ignores that attribute and writes the C#
/// member name instead — so a schema saying <c>"windows"</c> would go on the wire as
/// <c>"Windows"</c>. The schema is the contract, so this converter honours it, and
/// <c>tools/DiffHacker.SchemaGen</c> substitutes it into the generated code.
/// </para>
/// <para>
/// This matters well beyond casing: from Iteration 7 the LLM is asked to produce JSON against
/// these same schemas, and the host must accept exactly what the schema advertises.
/// </para>
/// </summary>
public sealed class SchemaEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToWire = BuildToWire();
    private static readonly Dictionary<string, TEnum> FromWire = BuildFromWire();

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string for {typeof(TEnum).Name} but found {reader.TokenType}.");
        }

        var value = reader.GetString()!;
        if (FromWire.TryGetValue(value, out var parsed))
        {
            return parsed;
        }

        throw new JsonException(
            $"'{value}' is not a valid {typeof(TEnum).Name}. Expected one of: {string.Join(", ", FromWire.Keys)}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!ToWire.TryGetValue(value, out var wire))
        {
            throw new JsonException($"{value} is not a declared value of {typeof(TEnum).Name}.");
        }

        writer.WriteStringValue(wire);
    }

    private static Dictionary<TEnum, string> BuildToWire()
    {
        var map = new Dictionary<TEnum, string>();

        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            map[(TEnum)field.GetValue(null)!] = WireName(field);
        }

        return map;
    }

    private static Dictionary<string, TEnum> BuildFromWire()
    {
        // Ordinal, not case-insensitive: the schema's spelling is the contract.
        var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);

        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            map[WireName(field)] = (TEnum)field.GetValue(null)!;
        }

        return map;
    }

    private static string WireName(FieldInfo field) =>
        field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name;
}
