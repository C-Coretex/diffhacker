using System.Globalization;
using System.Text;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using NJsonSchema.CodeGeneration.TypeScript;

namespace DiffHacker.SchemaGen;

/// <summary>
/// Turns every <c>*.schema.json</c> under <c>/schema</c> into C# records and TypeScript
/// interfaces. One parser feeds both languages, so the two cannot drift.
/// </summary>
internal sealed class ContractGenerator(Options options)
{
    private const string SchemaSuffix = ".schema.json";

    public async Task<int> RunAsync()
    {
        var version = ContractVersionFile.Read(options.SchemaDirectory);

        var schemaFiles = Directory
            .EnumerateFiles(options.SchemaDirectory, "*" + SchemaSuffix)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        if (schemaFiles.Length == 0)
        {
            throw new SchemaGenException($"No *{SchemaSuffix} files found in {options.SchemaDirectory}.");
        }

        var csharp = new OutputWriter(options.CSharpOutputDirectory, ".g.cs");
        var typescript = new OutputWriter(options.TypeScriptOutputDirectory, ".g.ts");
        var typeScriptModules = new List<string>();

        foreach (var schemaFile in schemaFiles)
        {
            var schema = await JsonSchema.FromFileAsync(schemaFile).ConfigureAwait(false);
            var typeName = ResolveTypeName(schema, schemaFile);
            var relativeSource = Path.GetFileName(schemaFile);

            csharp.Write(
                typeName + ".g.cs",
                Prelude(relativeSource, "//") + "#pragma warning disable\n\n" +
                    UseSchemaEnumConverter(
                        new CSharpGenerator(schema, CreateCSharpSettings()).GenerateFile(typeName)));

            var moduleName = ToCamelCase(typeName);
            typeScriptModules.Add(moduleName);
            typescript.Write(
                moduleName + ".g.ts",
                Prelude(relativeSource, "//") + "/* eslint-disable */\n\n" +
                    new TypeScriptGenerator(schema, CreateTypeScriptSettings()).GenerateFile(typeName));
        }

        csharp.Write("ContractVersion.g.cs", GenerateCSharpVersion(version));
        typescript.Write("contractVersion.g.ts", GenerateTypeScriptVersion(version));
        typeScriptModules.Add("contractVersion");

        // Named index.ts, not index.g.ts, so `import { HostInfo } from '@/contracts'` resolves
        // through the directory without an extra path alias.
        typescript.Write("index.ts", GenerateTypeScriptBarrel(typeScriptModules));

        csharp.RemoveStale();
        typescript.RemoveStale();

        Console.WriteLine(
            $"DiffHacker.SchemaGen: contract {version}, {schemaFiles.Length} schema(s), " +
            $"{csharp.Changed} C# and {typescript.Changed} TypeScript file(s) written.");

        WriteStamp(version, schemaFiles);
        return 0;
    }

    /// <summary>
    /// Redirects generated enum properties to <c>SchemaEnumConverter</c>.
    /// <para>
    /// NJsonSchema emits the schema's declared values as <c>[EnumMember]</c> and then points
    /// the property at <c>JsonStringEnumConverter</c>, which ignores that attribute and writes
    /// the C# member name — so <c>"windows"</c> in the schema would reach the wire as
    /// <c>"Windows"</c>. Substituting the converter keeps the schema authoritative.
    /// </para>
    /// </summary>
    private string UseSchemaEnumConverter(string generated) =>
        generated.Replace(
            "System.Text.Json.Serialization.JsonStringEnumConverter<",
            $"global::{options.Namespace}.SchemaEnumConverter<",
            StringComparison.Ordinal);

    private static string ResolveTypeName(JsonSchema schema, string schemaFile)
    {
        if (!string.IsNullOrWhiteSpace(schema.Title))
        {
            return schema.Title;
        }

        throw new SchemaGenException(
            $"{Path.GetFileName(schemaFile)} has no 'title'. The title is the generated type name.");
    }

    private CSharpGeneratorSettings CreateCSharpSettings() => new()
    {
        Namespace = options.Namespace,
        TypeNameGenerator = new TitleFirstTypeNameGenerator(),
        ClassStyle = CSharpClassStyle.Record,
        JsonLibrary = CSharpJsonLibrary.SystemTextJson,
        GenerateNullableReferenceTypes = true,
        GenerateOptionalPropertiesAsNullable = true,
        RequiredPropertiesMustBeDefined = true,
        GenerateDataAnnotations = false,
        GenerateJsonMethods = false,
        GenerateDefaultValues = true,
        ArrayType = "System.Collections.Generic.IReadOnlyList",
        ArrayInstanceType = "System.Collections.Generic.List",
        DictionaryType = "System.Collections.Generic.IReadOnlyDictionary",
        DictionaryInstanceType = "System.Collections.Generic.Dictionary",
    };

    private static TypeScriptGeneratorSettings CreateTypeScriptSettings() => new()
    {
        TypeNameGenerator = new TitleFirstTypeNameGenerator(),
        TypeStyle = TypeScriptTypeStyle.Interface,
        TypeScriptVersion = 5.0m,
        NullValue = TypeScriptNullValue.Undefined,
        MarkOptionalProperties = true,
        DateTimeType = TypeScriptDateTimeType.String,
        EnumStyle = TypeScriptEnumStyle.StringLiteral,
        GenerateCloneMethod = false,
        GenerateConstructorInterface = false,
        ExportTypes = true,
    };

    private static string Prelude(string sourceFile, string comment) =>
        $"""
         {comment} <auto-generated />
         {comment}
         {comment} Generated by tools/DiffHacker.SchemaGen from schema/{sourceFile}.
         {comment} Do not edit. Change the JSON Schema and rebuild instead.

         """;

    private string GenerateCSharpVersion(string version) =>
        Prelude(ContractVersionFile.FileName, "//") +
        $$"""

          namespace {{options.Namespace}};

          /// <summary>
          /// Version of the /schema contract set this assembly was built against.
          /// </summary>
          public static class ContractVersion
          {
              /// <summary>The canonical contract version, from schema/contract-version.json.</summary>
              public const string Current = "{{version}}";
          }

          """;

    private static string GenerateTypeScriptVersion(string version) =>
        Prelude(ContractVersionFile.FileName, "//") +
        $"""

         /** Version of the /schema contract set this bundle was built against. */
         export const CONTRACT_VERSION = '{version}';

         """;

    private static string GenerateTypeScriptBarrel(IEnumerable<string> modules)
    {
        var builder = new StringBuilder(Prelude("*" + SchemaSuffix, "//"));
        builder.Append('\n');

        foreach (var module in modules.Distinct(StringComparer.Ordinal).OrderBy(static m => m, StringComparer.Ordinal))
        {
            builder.Append(CultureInfo.InvariantCulture, $"export * from './{module}.g';\n");
        }

        return builder.ToString();
    }

    private void WriteStamp(string version, string[] schemaFiles)
    {
        if (options.StampFile is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.StampFile)!);
        File.WriteAllText(
            options.StampFile,
            $"{version}\n{schemaFiles.Length}\n{DateTimeOffset.UtcNow:O}\n");
    }

    private static string ToCamelCase(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
