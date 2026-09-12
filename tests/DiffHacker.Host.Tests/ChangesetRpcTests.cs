using System.Text;
using System.Text.Json;
using DiffHacker.Core.Changes;
using DiffHacker.Host.Editor;
using DiffHacker.Host.Rpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The changeset as the renderer sees it: over the bridge, as JSON, resolved through the
/// contract rather than through C# types on both ends.
/// <para>
/// A stub <see cref="IGitClient"/> rather than a fixture repository — the git behaviour is
/// covered exhaustively in <c>DiffHacker.Git.Tests</c>, and what is worth checking here is the
/// translation: statuses spelled as the schema spells them, a clean tree arriving as a result
/// rather than an error, and a failure carrying a code the renderer can look up.
/// </para>
/// <para>
/// <c>saveFileContent</c> is the exception: it goes through a real <see cref="RepositoryWorkingTreeWriter"/>
/// against a real temporary repository rather than the git stub, because what is worth checking
/// there is that bytes actually land on disk and that a mismatched expected content actually
/// refuses to write them.
/// </para>
/// </summary>
public sealed class ChangesetRpcTests : IAsyncLifetime
{
    private readonly FakeAppShell _shell = new();
    private readonly RpcNotifier _notifier = new(NullLogger<RpcNotifier>.Instance);
    private readonly StubGitClient _git = new();
    private readonly TemporaryRepository _repository = TemporaryRepository.CreateWithCommit();
    private RpcBridge _bridge = null!;

    public ValueTask InitializeAsync()
    {
        _bridge = new RpcBridge(
            _shell,
            _notifier,
            [
                new ChangesetRpcTarget(
                    _git,
                    new RepositoryWorkingTreeWriter(NullLogger<RepositoryWorkingTreeWriter>.Instance),
                    NullLogger<ChangesetRpcTarget>.Instance),
            ],
            NullLogger<RpcBridge>.Instance);

        _bridge.Start();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _bridge.DisposeAsync();
        _shell.Dispose();
        _repository.Dispose();
    }

    [Fact]
    public async Task Load_returns_the_files_with_statuses_spelled_the_way_the_schema_spells_them()
    {
        _git.Changeset = new Changeset
        {
            RepositoryPath = "/repo",
            IsClean = false,
            HasCommits = true,
            UntrackedIncluded = true,
            HunkCountsAvailable = true,
            Files =
            [
                new ChangedFile
                {
                    Path = "src/new.ts",
                    Status = ChangeStatus.Renamed,
                    PreviousPath = "src/old.ts",
                    LinesAdded = 3,
                    LinesRemoved = 1,
                    HunkCount = 2,
                    IsBinary = false,
                    Language = "TypeScript",
                    Project = new ProjectReference("Web", "src", "src/package.json"),
                },
            ],
            Statistics = ChangesetStatistics.From(
            [
                new ChangedFile
                {
                    Path = "src/new.ts",
                    Status = ChangeStatus.Renamed,
                    LinesAdded = 3,
                    LinesRemoved = 1,
                    IsBinary = false,
                    Language = "TypeScript",
                    Project = new ProjectReference("Web", "src", "src/package.json"),
                },
            ]),
        };

        _shell.Receive(
            """
            {"jsonrpc":"2.0","id":1,"method":"changeset.load","params":[
              {"repositoryPath":"/repo","includeUntracked":true}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var result = response.RootElement.GetProperty("result");

        result.GetProperty("isClean").GetBoolean().ShouldBeFalse();

        var file = result.GetProperty("files")[0];
        file.GetProperty("status").GetString().ShouldBe("renamed", "The schema's spelling wins, not the C# member name.");
        file.GetProperty("previousPath").GetString().ShouldBe("src/old.ts");
        file.GetProperty("hunkCount").GetInt32().ShouldBe(2);
        file.GetProperty("project").GetString().ShouldBe("Web");
        file.GetProperty("projectManifest").GetString().ShouldBe("src/package.json");

        result.GetProperty("statistics").GetProperty("renamedFiles").GetInt32().ShouldBe(1);
        result.GetProperty("statistics").GetProperty("languages")[0].GetString().ShouldBe("TypeScript");
    }

    [Fact]
    public async Task A_clean_working_tree_is_a_result_and_not_an_error()
    {
        _git.Changeset = new Changeset
        {
            RepositoryPath = "/repo",
            IsClean = true,
            HasCommits = true,
            UntrackedIncluded = true,
            HunkCountsAvailable = true,
            Files = [],
            Statistics = ChangesetStatistics.From([]),
        };

        _shell.Receive(
            """
            {"jsonrpc":"2.0","id":2,"method":"changeset.load","params":[
              {"repositoryPath":"/repo","includeUntracked":true}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));

        // Requirement 9. Throwing here would make "nothing to review" indistinguishable from
        // "the app broke", and the renderer would show a failure for good news.
        response.RootElement.TryGetProperty("error", out _).ShouldBeFalse();
        response.RootElement.GetProperty("result").GetProperty("isClean").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task An_absent_side_arrives_as_a_kind_rather_than_a_missing_field()
    {
        _git.Content = FileContentResult.Absent();

        _shell.Receive(
            """
            {"jsonrpc":"2.0","id":3,"method":"changeset.fileContent","params":[
              {"repositoryPath":"/repo","path":"gone.cs","side":"head"}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var result = response.RootElement.GetProperty("result");

        result.GetProperty("kind").GetString().ShouldBe("absent");
        result.TryGetProperty("text", out _).ShouldBeFalse("Null properties are not written to the wire.");
        _git.LastSide.ShouldBe(FileSide.Head);
    }

    [Fact]
    public async Task An_oversized_diff_reports_its_true_size_and_no_content()
    {
        _git.Diff = new FileDiffResult
        {
            Kind = FileContentKind.TooLarge,
            Path = "generated.min.js",
            SizeBytes = 42_000_000,
        };

        _shell.Receive(
            """
            {"jsonrpc":"2.0","id":4,"method":"changeset.fileDiff","params":[
              {"repositoryPath":"/repo","path":"generated.min.js","untracked":false}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var result = response.RootElement.GetProperty("result");

        result.GetProperty("kind").GetString().ShouldBe("too_large");
        result.GetProperty("sizeBytes").GetInt64().ShouldBe(42_000_000);
        result.TryGetProperty("unifiedDiff", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(GitClientFailure.GitUnavailable, "git_not_found")]
    [InlineData(GitClientFailure.RepositoryUnreadable, "changeset_repository_unreadable")]
    [InlineData(GitClientFailure.GitFailed, "changeset_git_failed")]
    public async Task A_failure_carries_a_stable_code_and_never_gits_own_words(
        GitClientFailure failure,
        string expectedCode)
    {
        _git.Failure = new GitClientException("fatal: not a git repository (or any parent up to /)", failure);

        _shell.Receive(
            """
            {"jsonrpc":"2.0","id":5,"method":"changeset.load","params":[
              {"repositoryPath":"/nowhere","includeUntracked":true}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var error = response.RootElement.GetProperty("error");

        error.GetProperty("code").GetInt32().ShouldBe(RpcErrors.ApplicationErrorCode);

        // §0.6: the host sends a code, the renderer owns the wording. Git's stderr goes to
        // log.txt and no further.
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe(expectedCode);
    }

    [Fact]
    public async Task Saving_writes_the_edited_text_and_returns_its_new_size()
    {
        _repository.Write("src/app.ts", "old\n");
        var path = JsonSerializer.Serialize(_repository.Root);

        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":10,"method":"changeset.saveFileContent","params":[
              {"repositoryPath":{{path}},"path":"src/app.ts","content":"new\n","expectedContent":"old\n"}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var result = response.RootElement.GetProperty("result");

        result.GetProperty("sizeBytes").GetInt64().ShouldBe(4);

        (await File.ReadAllTextAsync(
                Path.Combine(_repository.Root, "src", "app.ts"), TestContext.Current.CancellationToken))
            .ShouldBe("new\n", "The file on disk is the reviewer's edit, not what git last committed.");
    }

    [Fact]
    public async Task A_save_whose_expected_text_no_longer_matches_the_disk_is_refused()
    {
        _repository.Write("src/app.ts", "old\n");
        var path = JsonSerializer.Serialize(_repository.Root);

        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":11,"method":"changeset.saveFileContent","params":[
              {"repositoryPath":{{path}},"path":"src/app.ts","content":"new\n",
               "expectedContent":"something else entirely\n"}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var error = response.RootElement.GetProperty("error");

        // The gate is over the content, not over the intent: something changed the file since the
        // editor read it, so the reviewer's edit is refused rather than silently winning.
        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("changeset_save_conflict");

        (await File.ReadAllTextAsync(
                Path.Combine(_repository.Root, "src", "app.ts"), TestContext.Current.CancellationToken))
            .ShouldBe("old\n");
    }

    [Fact]
    public async Task A_path_that_escapes_the_repository_is_refused()
    {
        var path = JsonSerializer.Serialize(_repository.Root);

        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":12,"method":"changeset.saveFileContent","params":[
              {"repositoryPath":{{path}},"path":"../escape.txt","content":"x","expectedContent":""}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        var error = response.RootElement.GetProperty("error");

        error.GetProperty("data").GetProperty("code").GetString().ShouldBe("changeset_save_outside_repository");
        File.Exists(Path.Combine(Path.GetDirectoryName(_repository.Root)!, "escape.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task Saving_a_path_with_no_working_tree_file_yet_creates_it()
    {
        var path = JsonSerializer.Serialize(_repository.Root);

        // The editor's working-tree side of a deleted or brand-new file is empty text, so empty
        // expected content is what "nothing here yet" looks like from the caller's side too.
        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":13,"method":"changeset.saveFileContent","params":[
              {"repositoryPath":{{path}},"path":"new/nested/file.txt","content":"created\n","expectedContent":""}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        response.RootElement.TryGetProperty("error", out _).ShouldBeFalse();

        (await File.ReadAllTextAsync(
                Path.Combine(_repository.Root, "new", "nested", "file.txt"), TestContext.Current.CancellationToken))
            .ShouldBe("created\n");
    }

    [Fact]
    public async Task Saving_with_a_named_encoding_writes_that_encoding_back_to_disk()
    {
        var path = JsonSerializer.Serialize(_repository.Root);

        _shell.Receive(
            $$"""
            {"jsonrpc":"2.0","id":14,"method":"changeset.saveFileContent","params":[
              {"repositoryPath":{{path}},"path":"legacy.txt","content":"café\n","expectedContent":"",
               "encoding":"iso-8859-1"}]}
            """);

        using var response = JsonDocument.Parse(await _shell.NextSentAsync(TestContext.Current.CancellationToken));
        response.RootElement.TryGetProperty("error", out _).ShouldBeFalse();

        var bytes = await File.ReadAllBytesAsync(
            Path.Combine(_repository.Root, "legacy.txt"), TestContext.Current.CancellationToken);

        bytes.ShouldBe(
            Encoding.Latin1.GetBytes("café\n"),
            "A file the editor read as Latin-1 has to be written back as Latin-1, not silently turned into UTF-8.");
    }

    private sealed class StubGitClient : IGitClient
    {
        public Changeset? Changeset { get; set; }

        public FileDiffResult? Diff { get; set; }

        public FileContentResult? Content { get; set; }

        public GitClientException? Failure { get; set; }

        public FileSide LastSide { get; private set; }

        public Task<Changeset> GetChangesetAsync(ChangesetQuery query, CancellationToken cancellationToken) =>
            Failure is not null
                ? Task.FromException<Changeset>(Failure)
                : Task.FromResult(Changeset!);

        public Task<FileDiffResult> GetFileDiffAsync(FileDiffQuery query, CancellationToken cancellationToken) =>
            Failure is not null
                ? Task.FromException<FileDiffResult>(Failure)
                : Task.FromResult(Diff!);

        public Task<FileContentResult> GetFileContentAsync(
            FileContentQuery query,
            CancellationToken cancellationToken)
        {
            LastSide = query.Side;

            return Failure is not null
                ? Task.FromException<FileContentResult>(Failure)
                : Task.FromResult(Content!);
        }

        // Iteration 5 added these for the toolbox. The changeset RPC surface does not use them,
        // and a stub that pretended to would invite a test to depend on a fiction.
        public Task<IReadOnlyList<string>> ListFilesAsync(
            FileListQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The changeset RPC surface does not list files.");

        public Task<GrepResult> GrepAsync(GrepQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The changeset RPC surface does not search.");

        // Iteration 6 added these for the project profile's drift check. Same reasoning again.
        public Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The changeset RPC surface does not read history.");

        public Task<CommitComparison> CompareWithHeadAsync(
            string repositoryPath,
            string commitSha,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The changeset RPC surface does not compare commits.");
    }
}
