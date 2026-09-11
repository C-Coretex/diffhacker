using System.Security.Cryptography;
using System.Text;
using DiffHacker.Core.Changes;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Git;

public sealed partial class GitClient
{
    /// <summary>
    /// How many files are read at once. Enough to keep a disk busy, few enough that a 1500-file
    /// changeset does not open 1500 handles.
    /// </summary>
    private const int HashConcurrency = 8;

    /// <summary>
    /// Stamps each file with a SHA-256 of its working-tree side.
    /// <para>
    /// Read by .NET rather than asked of git: <c>git hash-object</c> can write to the object
    /// database, which is why it is not on the allowlist, and the working-tree side of a modified file
    /// is all zeros in <c>--raw</c> because git does not hash what it has not staged. This reads only
    /// the files the changeset already names, under the root, and never follows a link out of it.
    /// </para>
    /// </summary>
    private async Task<List<ChangedFile>> HashContentAsync(
        string root,
        List<ChangedFile> files,
        CancellationToken cancellationToken)
    {
        var hashes = new string?[files.Count];

        await Parallel.ForEachAsync(
                Enumerable.Range(0, files.Count),
                new ParallelOptions { MaxDegreeOfParallelism = HashConcurrency, CancellationToken = cancellationToken },
                async (index, token) => hashes[index] = await HashAsync(root, files[index], token).ConfigureAwait(false))
            .ConfigureAwait(false);

        return [.. files.Select((file, index) => file with { ContentSha256 = hashes[index] })];
    }

    private async Task<string?> HashAsync(string root, ChangedFile file, CancellationToken cancellationToken)
    {
        if (file.Status is ChangeStatus.Deleted || file.IsNestedRepository)
        {
            return null;
        }

        if (file.IsSubmodule)
        {
            return file.SubmoduleToCommit is { } commit ? HashText("submodule:" + commit) : null;
        }

        var path = RepositoryPaths.ToAbsolute(root, file.Path);

        if (!RepositoryPaths.Contains(root, path))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);

            // A link is identified by where it points, which is what git records for one. Following
            // it would hash a file that may not be in the repository at all.
            if (info.LinkTarget is { } target)
            {
                return HashText("symlink:" + target);
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(digest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Gone between the diff and the read, or locked. Null means "compare this one by its
            // line counts", which is weaker but honest; failing the whole changeset over it is not.
            ContentUnhashable(logger, file.Path, ex.GetType().Name);
            return null;
        }
    }

    private static string HashText(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [LoggerMessage(
        EventId = 2034,
        Level = LogLevel.Debug,
        Message = "Could not hash {Path} for the changeset fingerprint ({Reason}).")]
    private static partial void ContentUnhashable(ILogger logger, string path, string reason);
}
