using System.Text.Json;
using DiffHacker.Core.Changes;
using DiffHacker.Git;
using DiffHacker.Host.Rpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The whole stack, minus the pixels: a real repository, the real <see cref="GitClient"/>, the
/// real JSON-RPC bridge, and JSON in and out.
/// <para>
/// Iteration 3's "Done when" is that the application displays the changed-file list for the
/// current working tree and can produce the diff for any file in it. The unit suites prove each
/// layer; this proves they are actually connected, against a working tree carrying every
/// awkward case at once.
/// </para>
/// </summary>
public sealed class ChangesetFullStackTests : IAsyncLifetime
{
    private readonly FakeAppShell _shell = new();
    private readonly RpcNotifier _notifier = new(NullLogger<RpcNotifier>.Instance);
    private RpcBridge _bridge = null!;

    public ValueTask InitializeAsync()
    {
        var runner = new GitProcessRunner(NullLogger<GitProcessRunner>.Instance);

        IGitClient git = new GitClient(
            runner,
            new GitEnvironment(runner, NullLogger<GitEnvironment>.Instance),
            NullLogger<GitClient>.Instance);

        _bridge = new RpcBridge(
            _shell,
            _notifier,
            [new ChangesetRpcTarget(git, NullLogger<ChangesetRpcTarget>.Instance)],
            NullLogger<RpcBridge>.Instance);

        _bridge.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _bridge.DisposeAsync();
        _shell.Dispose();
    }

    [Fact]
    public async Task The_changed_file_list_and_a_files_diff_both_come_back_over_the_bridge()
    {
        using var repository = BuildAwkwardWorkingTree();

        var path = JsonSerializer.Serialize(repository.Root);

        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":1,"method":"changeset.load","params":[
              {"repositoryPath":{{path}},"includeUntracked":true}]}
            """);

        using var loaded = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var result = loaded.RootElement.GetProperty("result");

        result.GetProperty("isClean").GetBoolean().ShouldBeFalse();

        var files = result.GetProperty("files").EnumerateArray()
            .ToDictionary(file => file.GetProperty("path").GetString()!, file => file);

        // §0.2.5, at the boundary the renderer actually reads. Every kind of change reached it.
        files.Keys.ShouldBe(
            ["edited.cs", "renamed.md", "removed.txt", "logo.png", "brand-new.ts"],
            ignoreOrder: true);

        files["edited.cs"].GetProperty("status").GetString().ShouldBe("modified");
        files["renamed.md"].GetProperty("status").GetString().ShouldBe("renamed");
        files["renamed.md"].GetProperty("previousPath").GetString().ShouldBe("original.md");
        files["removed.txt"].GetProperty("status").GetString().ShouldBe("deleted");
        files["logo.png"].GetProperty("isBinary").GetBoolean().ShouldBeTrue();
        files["logo.png"].TryGetProperty("linesAdded", out _)
            .ShouldBeFalse("A binary carries no line counts, and absent is how that crosses the wire.");
        files["brand-new.ts"].GetProperty("isUntracked").GetBoolean().ShouldBeTrue();
        files["brand-new.ts"].GetProperty("language").GetString().ShouldBe("TypeScript");

        result.GetProperty("statistics").GetProperty("totalFiles").GetInt32().ShouldBe(5);

        // And the diff for one of them, which is the other half of the "Done when" bar.
        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":2,"method":"changeset.fileDiff","params":[
              {"repositoryPath":{{path}},"path":"brand-new.ts","untracked":true}]}
            """);

        using var diff = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var patch = diff.RootElement.GetProperty("result");

        patch.GetProperty("kind").GetString().ShouldBe("text");
        patch.GetProperty("unifiedDiff").GetString()
            .ShouldNotBeNull()
            .ShouldContain("+export const value = 1;");
    }

    [Fact]
    public async Task A_gitignored_file_never_reaches_the_renderer()
    {
        using var repository = BuildAwkwardWorkingTree();

        var path = JsonSerializer.Serialize(repository.Root);

        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":3,"method":"changeset.load","params":[
              {"repositoryPath":{{path}},"includeUntracked":true}]}
            """);

        using var loaded = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));

        var paths = loaded.RootElement.GetProperty("result").GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString())
            .ToArray();

        paths.ShouldNotContain("ignored.env", "An ignored file is not part of the change in either mode.");
    }

    /// <summary>
    /// One working tree with a modification, a rename, a deletion, a binary, an untracked file
    /// and an ignored file — the awkward cases requirement 7 lists, all at once.
    /// </summary>
    private static TemporaryRepository BuildAwkwardWorkingTree()
    {
        var repository = TemporaryRepository.CreateWithCommit();

        repository.Write(".gitignore", "ignored.env\n");
        repository.Write("edited.cs", "class Edited { }\n");
        repository.Write("original.md", string.Join('\n', Enumerable.Range(0, 40).Select(i => $"line {i}")) + "\n");
        repository.Write("removed.txt", "goodbye\n");
        repository.Write("logo.png", "placeholder\n");
        repository.Commit("baseline");

        repository.Write("edited.cs", "class Edited { public int Value; }\n");
        repository.Git("mv", "original.md", "renamed.md");
        File.Delete(Path.Combine(repository.Root, "removed.txt"));

        // A NUL inside the first 8000 bytes is git's own binary test.
        File.WriteAllBytes(Path.Combine(repository.Root, "logo.png"), [0x89, 0x50, 0x00, 0x4E, 0x47, 0x0D]);

        repository.Write("brand-new.ts", "export const value = 1;\n");
        repository.Write("ignored.env", "TOKEN=hunter2\n");

        return repository;
    }
}
