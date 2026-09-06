using NJsonSchema;

namespace DiffHacker.SchemaGen;

/// <summary>
/// Makes a schema's <c>title</c> authoritative for the generated type name.
/// <para>
/// NJsonSchema's default generator names a type after the property that references it, so a
/// <c>$defs</c> entry titled <c>ChangedFileInfo</c> reached through a <c>files</c> array would be
/// generated as <c>Files</c>. Contract type names are part of the contract, so they must come
/// from the schema, not from whichever property happens to point at it first.
/// </para>
/// </summary>
internal sealed class TitleFirstTypeNameGenerator : DefaultTypeNameGenerator
{
    public override string Generate(
        JsonSchema schema,
        string? typeNameHint,
        IEnumerable<string> reservedTypeNames)
    {
        var title = schema.ActualTypeSchema?.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            typeNameHint = title;
        }

        return base.Generate(schema, typeNameHint, reservedTypeNames);
    }
}
