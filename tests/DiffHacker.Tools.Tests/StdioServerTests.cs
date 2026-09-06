using DiffHacker.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// The iteration's verification step 1, automated: start the real <c>diffhacker-mcp</c>
/// executable, speak MCP to it over stdio as a client would, and check every tool is there and
/// usable.
/// <para>
/// This is the test that makes "one definition, two consumers" mean something. Every other test
/// here drives the in-process projection; this one drives the wire, against a separate process,
/// through the actual SDK client — so a tool that works in-process and is broken over stdio
/// cannot pass unnoticed.
/// </para>
/// </summary>
public sealed class StdioServerTests
{
    private static string ServerExecutable()
    {
        // Beside this test assembly's output, via the ProjectReference that exists purely to
        // make `dotnet test` build the server first.
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))!)!;

        var candidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DiffHacker.Mcp", "bin", configuration, "net10.0",
            OperatingSystem.IsWindows() ? "diffhacker-mcp.exe" : "diffhacker-mcp"));

        return candidate;
    }

    private static async Task<McpClient> ConnectAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var executable = ServerExecutable();

        Assert.SkipUnless(
            File.Exists(executable),
            $"The MCP server was not built at {executable}. Build the solution and run again.");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "diffhacker",
            Command = executable,
            Arguments = ["--repository", repositoryPath],
        });

        return await McpClient.CreateAsync(
            transport,
            loggerFactory: NullLoggerFactory.Instance,
            cancellationToken: cancellationToken);
    }

    private static FixtureRepository Repository()
    {
        var repository = FixtureRepository.CreateWithCommit();

        repository.WriteFile("src/app.ts", "export function run() {\n  return 1;\n}\n");
        repository.Stage("src/app.ts");
        repository.Commit("baseline");
        repository.WriteFile("src/app.ts", "export function run() {\n  return 2;\n}\n");
        repository.WriteFile("src/added.ts", "export const added = true;\n");

        return repository;
    }

    [Fact]
    public async Task Every_tool_is_advertised_over_the_wire_with_a_usable_description()
    {
        var token = TestContext.Current.CancellationToken;

        using var repository = Repository();
        await using var client = await ConnectAsync(repository.Root, token);

        var tools = await client.ListToolsAsync(cancellationToken: token);

        tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ShouldBe(
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
        ]);

        foreach (var tool in tools)
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace();
            tool.Description!.Length.ShouldBeGreaterThan(200, $"{tool.Name} needs a description a model can use");
        }
    }

    [Fact]
    public async Task Tool_results_arrive_as_plain_text_not_as_encoded_json()
    {
        var token = TestContext.Current.CancellationToken;

        using var repository = Repository();
        await using var client = await ConnectAsync(repository.Root, token);

        var result = await client.CallToolAsync("list_changed_files", cancellationToken: token);
        var text = Text(result);

        text.ShouldContain("src/app.ts");
        text.ShouldContain("src/added.ts");

        // A string routed through JSON serialisation would arrive quoted, with \n instead of
        // newlines — twice the tokens and far harder to read. It nearly shipped that way.
        text.ShouldNotStartWith("\"");
        text.ShouldNotContain("\\n");
        text.ShouldContain("\n");
    }

    [Fact]
    public async Task The_wire_path_reads_files_diffs_and_searches()
    {
        var token = TestContext.Current.CancellationToken;

        using var repository = Repository();
        await using var client = await ConnectAsync(repository.Root, token);

        var read = await client.CallToolAsync(
            "read_file",
            new Dictionary<string, object?> { ["path"] = "src/app.ts" },
            cancellationToken: token);

        Text(read).ShouldContain("export function run()");

        var diff = await client.CallToolAsync(
            "get_file_diff",
            new Dictionary<string, object?> { ["paths"] = new[] { "src/app.ts" } },
            cancellationToken: token);

        Text(diff).ShouldContain("+  return 2;");

        var search = await client.CallToolAsync(
            "search_text",
            new Dictionary<string, object?> { ["pattern"] = "export" },
            cancellationToken: token);

        Text(search).ShouldContain("src/app.ts");
    }

    [Fact]
    public async Task The_sandbox_holds_over_the_wire_too()
    {
        var token = TestContext.Current.CancellationToken;

        using var repository = Repository();
        await using var client = await ConnectAsync(repository.Root, token);

        foreach (var path in new[] { "../../etc/passwd", ".git/config", "/etc/passwd" })
        {
            var result = await client.CallToolAsync(
                "read_file",
                new Dictionary<string, object?> { ["path"] = path },
                cancellationToken: token);

            var text = Text(result);
            text.ShouldNotContain("[core]");
            text.ShouldNotContain("root:");
        }
    }

    [Fact]
    public async Task The_server_refuses_a_path_that_is_not_a_repository_rather_than_serving_nothing()
    {
        var token = TestContext.Current.CancellationToken;

        using var directory = FixtureRepository.CreateEmptyDirectory();

        // McpClient.CreateAsync performs the handshake, and the server exits before that when it
        // cannot open the repository — so this surfaces as a connection failure, which is the
        // right shape: a client should not think it has a working toolbox.
        await Should.ThrowAsync<Exception>(async () =>
        {
            await using var client = await ConnectAsync(directory.Root, token);
            await client.ListToolsAsync(cancellationToken: token);
        });
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        string.Concat(result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(block => block.Text));
}
