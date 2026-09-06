using System.Reflection;
using DiffHacker.Contracts;

namespace DiffHacker.Contracts.Tests;

/// <summary>
/// The schemas, readable at run time as well as at build time.
/// <para>
/// Everywhere else a schema is a build-time input that becomes a C# record and a TypeScript
/// interface. From Iteration 4 the model is asked to answer in a shape one of these documents
/// defines, and the answer is validated against it — neither of which a generated record can
/// do. So the documents are embedded, and this checks that the embedding and the generation
/// stay in step.
/// </para>
/// </summary>
public sealed class ContractSchemasTests
{
    private static readonly string SchemaDirectory = typeof(ContractSchemasTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "DiffHacker.SchemaDir")
        .Value!;

    [Fact]
    public void Every_schema_file_is_embedded_under_its_own_name()
    {
        var onDisk = Directory.EnumerateFiles(SchemaDirectory, "*.schema.json")
            .Select(path => Path.GetFileName(path).Replace(".schema.json", string.Empty, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        onDisk.ShouldNotBeEmpty();
        ContractSchemas.Names.ShouldBe(onDisk, "a schema that is generated but not embedded would be unusable at run time.");
    }

    [Fact]
    public void An_embedded_schema_is_the_file_verbatim()
    {
        // Not a summary or a re-serialisation: the document the generator read and the document
        // the model is asked to satisfy have to be the same text, or they can drift.
        var expected = File.ReadAllText(Path.Combine(SchemaDirectory, "changeset-result.schema.json"));

        ContractSchemas.Get("changeset-result").ReplaceLineEndings().ShouldBe(expected.ReplaceLineEndings());
    }

    [Fact]
    public void An_unknown_name_fails_loudly()
    {
        // A typo here is a build-time mistake, and returning null would surface it much later
        // as an empty response format.
        Should.Throw<ArgumentException>(() => ContractSchemas.Get("no-such-schema"));
        ContractSchemas.Contains("no-such-schema").ShouldBeFalse();
    }

    [Fact]
    public void The_name_is_the_file_name_not_the_generated_type_name()
    {
        ContractSchemas.Contains("provider-profile-list").ShouldBeTrue();
        ContractSchemas.Contains("ProviderProfileList").ShouldBeFalse();
    }
}
