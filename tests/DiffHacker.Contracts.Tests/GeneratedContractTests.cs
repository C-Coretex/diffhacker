using System.Reflection;
using System.Text.Json;
using DiffHacker.Contracts;

namespace DiffHacker.Contracts.Tests;

/// <summary>
/// The generated contracts are the agreement between the host, the renderer and — from
/// Iteration 7 — the LLM. These tests check the pipeline produced something usable, not that
/// NJsonSchema works.
/// </summary>
public sealed class GeneratedContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Contract_version_matches_the_schema_directory()
    {
        var schemaDir = SchemaDirectory();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(schemaDir, "contract-version.json")));

        document.RootElement.GetProperty("version").GetString().ShouldBe(ContractVersion.Current);
    }

    [Fact]
    public void Every_schema_produced_a_type()
    {
        var generated = typeof(ContractVersion).Assembly.GetTypes()
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var schema in Directory.EnumerateFiles(SchemaDirectory(), "*.schema.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(schema));
            var title = document.RootElement.GetProperty("title").GetString()!;

            generated.ShouldContain(title, $"{Path.GetFileName(schema)} did not produce a type named {title}.");
        }
    }

    [Fact]
    public void HostInfo_round_trips_through_System_Text_Json()
    {
        var original = new HostInfo(
            appVersion: "0.1.0",
            contractVersion: ContractVersion.Current,
            osDescription: "Test OS 1.0",
            platform: HostInfoPlatform.Linux,
            processArchitecture: "arm64",
            selfTest: true,
            startedAtUtc: new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<HostInfo>(json, Options)!;

        restored.AppVersion.ShouldBe(original.AppVersion);
        restored.Platform.ShouldBe(HostInfoPlatform.Linux);
        restored.SelfTest.ShouldBeTrue();
        restored.StartedAtUtc.ShouldBe(original.StartedAtUtc);
    }

    [Fact]
    public void Wire_names_come_from_the_schema_not_from_CSharp_naming()
    {
        var json = JsonSerializer.Serialize(
            new StartDemoResponse(operationId: "abc", totalSteps: 3),
            Options);

        // camelCase as declared in the schema, regardless of serializer naming policy.
        json.ShouldContain("\"operationId\"");
        json.ShouldContain("\"totalSteps\"");
    }

    [Theory]
    [InlineData(HostInfoPlatform.Windows, "windows")]
    [InlineData(HostInfoPlatform.Macos, "macos")]
    [InlineData(HostInfoPlatform.Linux, "linux")]
    public void Enum_values_serialise_exactly_as_the_schema_spells_them(HostInfoPlatform platform, string expected)
    {
        // The C# member is Macos; the schema says "macos". The schema wins.
        var json = JsonSerializer.Serialize(
            new HostInfo("0.1.0", ContractVersion.Current, "os", platform, "x64", false, DateTimeOffset.UnixEpoch),
            Options);

        json.ShouldContain($"\"platform\":\"{expected}\"");
        JsonSerializer.Deserialize<HostInfo>(json, Options)!.Platform.ShouldBe(platform);
    }

    [Fact]
    public void An_undeclared_enum_value_is_rejected_rather_than_coerced()
    {
        var json = """
            {"contractVersion":"1.0.0","appVersion":"0.1.0","platform":"Windows",
             "osDescription":"os","processArchitecture":"x64",
             "startedAtUtc":"2026-09-05T00:00:00+00:00","selfTest":false}
            """;

        // "Windows" is the C# member name, not a schema value. Accepting it would let the two
        // spellings drift apart unnoticed.
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<HostInfo>(json, Options));
    }

    [Fact]
    public void Nested_definitions_keep_the_name_from_their_title()
    {
        // Guards TitleFirstTypeNameGenerator: without it this type is named after the
        // referencing property and becomes "Checks".
        var result = new SelfTestResult([new SelfTestCheck("detail", "rpc_round_trip", true)], true);

        result.Checks[0].ShouldBeOfType<SelfTestCheck>();
        result.Checks[0].Name.ShouldBe("rpc_round_trip");
    }

    [Fact]
    public void Optional_properties_are_nullable()
    {
        var error = new RpcErrorData(args: null, code: "rpc_timeout");

        error.Args.ShouldBeNull();
        error.Code.ShouldBe("rpc_timeout");
    }

    private static string SchemaDirectory()
    {
        var stamped = typeof(GeneratedContractTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "DiffHacker.SchemaDir")?.Value;

        if (!string.IsNullOrWhiteSpace(stamped) && Directory.Exists(stamped))
        {
            return stamped;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "schema");
            if (File.Exists(Path.Combine(candidate, "contract-version.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate /schema from " + AppContext.BaseDirectory);
    }
}
