using DiffHacker.Core.Changes;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.Editor;

/// <summary>
/// §0.2.12's second write path: the reviewer typed directly into the diff editor's working-tree
/// side and pressed Save. This writes exactly those bytes to exactly that file — no <c>git add</c>,
/// no commit, no touch of any other path. <see cref="Knowledge.RepositoryDocumentationWriter"/> is
/// the first path; <c>RepositoryWriteTests</c> asserts that the two of them are the only write
/// paths into a repository anywhere in <c>src/</c>.
/// <para>
/// The gate is the content the editor started from, not a boolean flag. The caller sends the
/// working-tree text as it was when the editor last loaded or saved it, and this refuses to write
/// if the file on disk no longer decodes to exactly that text — something else changed the file
/// since, and silently overwriting that change would be worse than refusing to save. A file that
/// does not exist compares as empty text, which is what an editor recreating a working-tree-deleted
/// file was shown.
/// </para>
/// </summary>
public sealed partial class RepositoryWorkingTreeWriter(ILogger<RepositoryWorkingTreeWriter> logger)
{
    public async Task<long> WriteAsync(
        string repositoryPath,
        string relativePath,
        string content,
        string expectedContent,
        string? encoding,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(expectedContent);

        if (!RepositoryPaths.IsRepositoryRelative(relativePath, out var normalised))
        {
            throw new WorkingTreeWriteException(
                WorkingTreeWriteFailures.OutsideRepository,
                $"'{relativePath}' is not a repository-relative path.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        var absolute = Path.GetFullPath(RepositoryPaths.ToAbsolute(root, normalised));

        // Containment even though IsRepositoryRelative already rejected '..' segments: that check
        // is string-only, and this is the one place a reviewer's own keystrokes reach the working
        // tree. It does not get to rely on its caller any more than the documentation export does.
        if (!RepositoryPaths.Contains(root, absolute))
        {
            throw new WorkingTreeWriteException(
                WorkingTreeWriteFailures.OutsideRepository,
                $"'{relativePath}' resolves outside the repository.");
        }

        await RequireUnchangedAsync(absolute, relativePath, expectedContent, cancellationToken)
            .ConfigureAwait(false);

        var bytes = TextDecoding.Encode(content, encoding);

        try
        {
            if (Path.GetDirectoryName(absolute) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(absolute, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkingTreeWriteException(WorkingTreeWriteFailures.WriteFailed, ex.Message);
        }

        SavedEdit(logger, relativePath, root);

        return bytes.LongLength;
    }

    /// <summary>
    /// Refuses the write unless the file on disk still decodes to exactly the text the editor
    /// started from — decoded the same way <c>changeset.fileContent</c> decodes it, so a file the
    /// editor loaded unchanged compares equal whatever bytes it happens to be stored in.
    /// </summary>
    private static async Task RequireUnchangedAsync(
        string absolute,
        string relativePath,
        string expectedContent,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(absolute))
        {
            if (expectedContent.Length == 0)
            {
                return;
            }

            throw Conflict(relativePath);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(absolute, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkingTreeWriteException(WorkingTreeWriteFailures.WriteFailed, ex.Message);
        }

        // The editor cannot have loaded binary content as text, so a file that now looks binary has
        // changed kind since — exactly the case this check exists to catch.
        if (TextDecoding.LooksBinary(bytes))
        {
            throw Conflict(relativePath);
        }

        var currentText = TextDecoding.Decode(bytes, out _, out _);

        if (!string.Equals(currentText, expectedContent, StringComparison.Ordinal))
        {
            throw Conflict(relativePath);
        }
    }

    private static WorkingTreeWriteException Conflict(string relativePath) =>
        new(WorkingTreeWriteFailures.Conflict, $"'{relativePath}' has changed on disk since it was last read.");

    [LoggerMessage(
        EventId = 6020,
        Level = LogLevel.Warning,
        Message = "Saved an edit to {Path} in {Repository}. This is the second of the two write paths into a repository, and it ran because the reviewer typed into the diff editor and pressed Save.")]
    private static partial void SavedEdit(ILogger logger, string path, string repository);
}

/// <summary>Failure codes the renderer resolves through its own catalogue.</summary>
public static class WorkingTreeWriteFailures
{
    /// <summary>The path is not repository-relative, or resolves outside the repository root.</summary>
    public const string OutsideRepository = "changeset_save_outside_repository";

    /// <summary>The file on disk no longer matches what the editor started from.</summary>
    public const string Conflict = "changeset_save_conflict";

    /// <summary>The path resolved correctly and the write still failed.</summary>
    public const string WriteFailed = "changeset_save_failed";

    /// <summary>Every code above, for the test that asserts the taxonomy and the catalogue agree.</summary>
    public static IReadOnlyList<string> All { get; } = [OutsideRepository, Conflict, WriteFailed];
}

/// <summary>
/// Where a save failed, as a typed answer rather than an exception the interface would have to
/// parse a message out of.
/// </summary>
public sealed class WorkingTreeWriteException(string failure, string message) : Exception(message)
{
    /// <summary>One of <see cref="WorkingTreeWriteFailures"/>.</summary>
    public string Failure { get; } = failure;
}
