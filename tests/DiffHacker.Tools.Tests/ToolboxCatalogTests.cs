using System.Text.Json;
using DiffHacker.TestSupport;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// The iteration's verification step 8: one definition per tool serves both the in-process and
/// stdio paths.
/// <para>
/// The catalogue builds its two projections from one <c>MethodInfo</c> each, but they go through
/// different SDK factories, so "one definition" is only true if the two agree about what the
/// model sees. That is what these assert — name, description and argument schema, tool by tool.
/// Without them the guarantee would be a comment.
/// </para>
/// </summary>
public sealed class ToolboxCatalogTests
{
    private static readonly string[] Expected =
    [
        "find_files",
        "get_file_diff",
        "get_path_info",
        "get_project_profile",
        "get_repository_tree",
        "list_changed_files",
        "list_directory",
        "read_file",
        "report_progress",
        "search_text",
    ];

    [Fact]
    public async Task Every_tool_the_iteration_requires_exists()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        toolbox.Catalogue.Names.Order(StringComparer.Ordinal).ShouldBe(Expected);
    }

    [Fact]
    public async Task The_two_projections_expose_the_same_tools()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var mcp = toolbox.Catalogue.McpTools.Select(tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal);
        var llm = toolbox.Catalogue.LlmTools.Select(tool => tool.Name).Order(StringComparer.Ordinal);

        llm.ShouldBe(mcp);
    }

    [Fact]
    public async Task The_two_projections_agree_on_description_and_argument_schema()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        foreach (var mcp in toolbox.Catalogue.McpTools)
        {
            var llm = toolbox.Catalogue.LlmTools.Single(tool => tool.Name == mcp.ProtocolTool.Name);

            llm.Description.ShouldBe(mcp.ProtocolTool.Description);

            // Compared as parsed JSON: two schemas that differ only in whitespace are the same
            // schema, and comparing the text would fail for no reason a reader would accept.
            Normalise(llm.ParametersSchemaJson)
                .ShouldBe(Normalise(mcp.ProtocolTool.InputSchema.GetRawText()), $"schema for {llm.Name}");
        }
    }

    [Fact]
    public async Task Every_tool_has_a_description_written_for_a_model_to_read()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        foreach (var tool in toolbox.Catalogue.LlmTools)
        {
            // The iteration calls these a deliverable rather than boilerplate. A one-line
            // description is the shape that produces a bad graph and looks like a prompt problem.
            tool.Description.Length.ShouldBeGreaterThan(200, $"{tool.Name} needs a usable description");
        }
    }

    [Fact]
    public async Task Every_argument_is_described()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        foreach (var tool in toolbox.Catalogue.LlmTools)
        {
            using var schema = JsonDocument.Parse(tool.ParametersSchemaJson);

            if (!schema.RootElement.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject())
            {
                property.Value.TryGetProperty("description", out var description).ShouldBeTrue(
                    $"{tool.Name}.{property.Name} has no description, so the model is guessing");

                description.GetString().ShouldNotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task No_tool_takes_a_repository_path()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        foreach (var tool in toolbox.Catalogue.LlmTools)
        {
            using var schema = JsonDocument.Parse(tool.ParametersSchemaJson);

            if (!schema.RootElement.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            // Argument names, not the whole document: descriptions say "repository-relative"
            // constantly and should. The repository is fixed when the toolbox opens, and a tool
            // that accepted one would be a way out of the sandbox no path check could close.
            foreach (var property in properties.EnumerateObject())
            {
                property.Name.ShouldNotContain("repository", Case.Insensitive, $"in {tool.Name}");
                property.Name.ShouldNotContain("root", Case.Insensitive, $"in {tool.Name}");
            }
        }
    }

    [Fact]
    public async Task No_tool_is_called_submit_result()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        // Reserved by DiffHacker.Llm's structured-output path, which intercepts a tool of this
        // name and treats its arguments as the final answer.
        toolbox.Catalogue.Names.ShouldNotContain("submit_result");
    }

    [Fact]
    public async Task An_unparseable_argument_object_is_a_failure_the_model_can_read()
    {
        using var repository = FixtureRepository.CreateWithCommit();
        await using var toolbox = await ToolboxFixture.OpenAsync(repository, TestContext.Current.CancellationToken);

        var tool = toolbox.Catalogue.LlmTools.Single(t => t.Name == "read_file");
        var result = await tool.Invoke("{ not json", TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.Content.ShouldContain("The tool failed");
    }

    private static string Normalise(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
