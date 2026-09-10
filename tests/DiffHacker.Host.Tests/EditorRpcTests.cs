using DiffHacker.Contracts;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Settings;
using DiffHacker.Host.Editor;
using DiffHacker.Host.Rpc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace DiffHacker.Host.Tests;

/// <summary>
/// The external-editor surface.
/// <para>
/// No test here launches a real editor. What is worth asserting is the part that goes wrong on a
/// user's machine rather than on a developer's: a <c>code</c> command that is not there, a custom
/// command that was typed wrong, and a committed side extracted to somewhere that is not the
/// repository. Requirement 3's "handle VS Code not being installed" is the second test below, and it
/// is exercised by pointing the custom command at an executable that genuinely does not exist —
/// which is what a renamed <c>code</c> arrives as.
/// </para>
/// </summary>
public sealed class EditorRpcTests : IAsyncLifetime
{
    private readonly DirectoryInfo _dataDirectory =
        Directory.CreateTempSubdirectory("diffhacker-editor-rpc-");

    private readonly StubGitClient _git = new();

    private AppPaths _paths = null!;
    private Storage.AppDatabase _database = null!;
    private IAppSettingStore _settings = null!;
    private EditorRpcTarget _target = null!;

    public ValueTask InitializeAsync()
    {
        _paths = new AppPaths(_dataDirectory.FullName);
        _database = new Storage.AppDatabase(_paths.DatabaseFile, NullLogger<Storage.AppDatabase>.Instance);
        _settings = new Storage.SqliteAppSettingStore(_database);

        var locator = new ExternalEditorLocator();

        _target = new EditorRpcTarget(
            locator,
            new ExternalEditorLauncher(
                locator,
                new HeadBlobExtractor(_git, _paths),
                _git,
                NullLogger<ExternalEditorLauncher>.Instance),
            _settings,
            NullLogger<EditorRpcTarget>.Instance);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();

        try
        {
            _dataDirectory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A file handle outliving the test is not a test failure.
        }
    }

    [Fact]
    public async Task An_unconfigured_machine_reports_no_custom_command_rather_than_a_default_one()
    {
        var described = await _target.DescribeAsync(TestContext.Current.CancellationToken);

        // Whether VS Code and Visual Studio are installed depends on the machine running the test,
        // so neither is asserted. What is asserted is that the answer is a fact rather than a guess:
        // nothing was configured, so nothing is reported as configured.
        described.CustomDiffCommand.ShouldBeEmpty();
        described.CustomOpenCommand.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_editor_that_is_not_installed_comes_back_as_its_own_code_rather_than_a_crash()
    {
        // Verification step 9, as a test. A `code` CLI that has been renamed away is exactly this:
        // an executable name that resolves to nothing.
        await Configure("definitely-not-an-editor-8f2a --diff {left} {right}", string.Empty);

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await Open(OpenInEditorRequestEditor.Custom));

        var data = failure.ErrorData.ShouldBeOfType<RpcErrorData>();

        data.Code.ShouldBe(EditorFailures.NotFound);
        data.Args.ShouldNotBeNull()["path"].ShouldBe("src/Contract.cs");
    }

    [Fact]
    public async Task Asking_for_a_custom_editor_with_none_configured_says_that_and_not_not_found()
    {
        // Two different things a reviewer can fix in two different ways, so they are two codes.
        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await Open(OpenInEditorRequestEditor.Custom));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe(EditorFailures.NotConfigured);
    }

    [Theory]
    [InlineData("mytool {left}", "", "editor_invalid_diff_command")]
    [InlineData("", "mytool --line {line}", "editor_invalid_open_command")]
    public async Task A_command_template_with_no_placeholder_is_refused_before_it_is_stored(
        string diff,
        string open,
        string expectedCode)
    {
        var failure = await Should.ThrowAsync<LocalRpcException>(async () => await _target.SaveAsync(
            new SaveEditorSettingsRequest(customDiffCommand: diff, customOpenCommand: open),
            TestContext.Current.CancellationToken));

        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe(expectedCode);

        // And nothing was written, so the field the user is still looking at is the only place the
        // bad value exists.
        (await _target.DescribeAsync(TestContext.Current.CancellationToken))
            .CustomDiffCommand.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_saved_command_comes_back_from_the_database()
    {
        var saved = await _target.SaveAsync(
            new SaveEditorSettingsRequest(
                customDiffCommand: "  meld {left} {right}  ",
                customOpenCommand: "gedit {file}+{line}"),
            TestContext.Current.CancellationToken);

        saved.CustomDiffCommand.ShouldBe("meld {left} {right}");
        saved.CustomOpenCommand.ShouldBe("gedit {file}+{line}");

        (await _target.DescribeAsync(TestContext.Current.CancellationToken))
            .CustomDiffCommand.ShouldBe("meld {left} {right}");
    }

    [Fact]
    public async Task The_committed_side_is_extracted_into_the_data_directory_and_never_the_repository()
    {
        // §0.2.12, as an assertion about where the bytes land. The launch itself fails, because the
        // executable does not exist — and by then the extraction has already happened, which is
        // exactly the moment worth inspecting.
        await Configure("definitely-not-an-editor-8f2a --diff {left} {right}", string.Empty);

        await Should.ThrowAsync<LocalRpcException>(async () => await Open(OpenInEditorRequestEditor.Custom));

        var written = Directory
            .EnumerateFiles(_paths.DiffCacheDirectory, "*", SearchOption.AllDirectories)
            .ToArray();

        var extracted = written.ShouldHaveSingleItem();

        extracted.ShouldContain(Path.Combine("diff-cache", _git.Commit));
        extracted.ShouldEndWith("Contract.cs");
        File.ReadAllText(extracted).ShouldBe("the committed contract\n");

        // The repository the request named is a path this test never created, so anything landing
        // inside it would be a directory that should not exist.
        Directory.Exists(_git.RepositoryPath).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_with_no_committed_side_is_opened_rather_than_compared()
    {
        // An added or untracked file has no HEAD side. Asking an external tool to compare against
        // nothing shows an empty pane; the honest answer is to open the file itself, and the host
        // decides that from what exists rather than from what the renderer claimed.
        _git.Content = FileContentResult.Absent();

        await Configure(string.Empty, "definitely-not-an-editor-8f2a --goto {file}:{line}");

        var failure = await Should.ThrowAsync<LocalRpcException>(
            async () => await Open(OpenInEditorRequestEditor.Custom));

        // It reached the open path, not the diff path: the diff template is empty, and had it been
        // chosen the code would have been editor_not_configured instead.
        failure.ErrorData.ShouldBeOfType<RpcErrorData>().Code.ShouldBe(EditorFailures.NotFound);

        Directory.Exists(_paths.DiffCacheDirectory).ShouldBeFalse();
    }

    private async Task Configure(string diff, string open) =>
        await _target.SaveAsync(
            new SaveEditorSettingsRequest(customDiffCommand: diff, customOpenCommand: open),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Opens a file that exists on disk, so the working-tree side of the decision is real. The
    /// repository path is the temp data directory's sibling rather than a fiction, because
    /// <c>RepositoryPaths.ToAbsolute</c> is what turns it into the path the launcher checks.
    /// </summary>
    private async Task Open(OpenInEditorRequestEditor editor)
    {
        Directory.CreateDirectory(Path.Combine(_git.RepositoryPath, "src"));
        File.WriteAllText(Path.Combine(_git.RepositoryPath, "src", "Contract.cs"), "the working contract\n");

        try
        {
            await _target.OpenAsync(
                new OpenInEditorRequest(
                    editor: editor,
                    line: 42,
                    path: "src/Contract.cs",
                    previousPath: null,
                    repositoryPath: _git.RepositoryPath),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(_git.RepositoryPath, recursive: true);
        }
    }

    /// <summary>
    /// Only the two things the editor surface asks git for. Everything else throws rather than
    /// pretending, so a test cannot come to depend on a fiction.
    /// </summary>
    private sealed class StubGitClient : IGitClient
    {
        public string RepositoryPath { get; } =
            Path.Combine(Path.GetTempPath(), "diffhacker-editor-repo-" + Guid.NewGuid().ToString("N")[..8]);

        public string Commit { get; } = "abc123def456";

        public FileContentResult Content { get; set; } = new()
        {
            Kind = FileContentKind.Text,
            Text = "the committed contract\n",
            SizeBytes = 23,
            Encoding = "utf-8",
        };

        public Task<string?> GetHeadCommitAsync(string repositoryPath, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(Commit);

        public Task<FileContentResult> GetFileContentAsync(
            FileContentQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(Content);

        public Task<Changeset> GetChangesetAsync(ChangesetQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Opening an editor does not load a changeset.");

        public Task<FileDiffResult> GetFileDiffAsync(FileDiffQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("An external editor computes its own diff.");

        public Task<IReadOnlyList<string>> ListFilesAsync(
            FileListQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Opening an editor does not list files.");

        public Task<GrepResult> GrepAsync(GrepQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Opening an editor does not search.");

        public Task<CommitComparison> CompareWithHeadAsync(
            string repositoryPath,
            string commitSha,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Opening an editor does not compare commits.");
    }
}
