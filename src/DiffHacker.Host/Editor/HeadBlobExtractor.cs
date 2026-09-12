using System.Text;
using DiffHacker.Core.Changes;

namespace DiffHacker.Host.Editor;

/// <summary>
/// Writes a file's committed content to disk so an external editor has two sides to compare.
/// <para>
/// An external diff tool takes two paths, and the <c>HEAD</c> side of a change is a git object, not a
/// file. Something has to materialise it. This writes it under
/// <see cref="AppPaths.DiffCacheDirectory"/> — the application's own data directory, never the
/// repository, which is what keeps §0.2.12 true: this is not one of the two places DiffHacker
/// writes into a repository, the documentation export and <see cref="RepositoryWorkingTreeWriter"/>.
/// </para>
/// <para>
/// The file is named for the commit it came from, so the same file at the same commit is written once
/// and reused, and a file extracted at an older commit is never mistaken for the current one.
/// </para>
/// <para>
/// One honest limitation: <see cref="IGitClient.GetFileContentAsync"/> hands back decoded text, so
/// what lands here is UTF-8 regardless of how the committed bytes were encoded. For a Latin-1 file
/// the external editor sees the same characters written a different way, which is visible only if the
/// reviewer looks at the encoding indicator. Reproducing the original bytes would mean a second git
/// read that returns them, and the diff itself is identical either way.
/// </para>
/// </summary>
public sealed class HeadBlobExtractor(IGitClient git, AppPaths paths)
{
    /// <summary>
    /// The absolute path of the extracted file, or null when the file has no committed side — which
    /// is the ordinary answer for anything added or untracked, not a failure.
    /// </summary>
    public async Task<string?> ExtractAsync(
        string repositoryPath,
        string relativePath,
        string commitLabel,
        CancellationToken cancellationToken)
    {
        var content = await git
            .GetFileContentAsync(
                new FileContentQuery(repositoryPath, relativePath, FileSide.Head),
                cancellationToken)
            .ConfigureAwait(false);

        // Binary and too-large both have no text to write, and neither has any business in an
        // external text comparison. The caller opens the working-tree file on its own instead.
        if (content.Kind != FileContentKind.Text || content.Text is null)
        {
            return null;
        }

        var destination = Path.Combine(
            paths.DiffCacheDirectory,
            Sanitise(commitLabel),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        // Path.GetFullPath collapses any traversal the pieces above could have carried, and the
        // containment check is what makes that collapse load-bearing rather than decorative.
        var resolved = Path.GetFullPath(destination);
        var root = Path.GetFullPath(paths.DiffCacheDirectory);

        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ExternalEditorException(
                EditorFailures.LaunchFailed,
                $"The extracted path '{resolved}' escaped the diff cache.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);

        // Rewritten every time rather than reused on existence: the cost is a few kilobytes, and a
        // truncated file left behind by a previous crash would otherwise be shown as the truth about
        // a commit forever.
        await File.WriteAllTextAsync(resolved, content.Text, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        return resolved;
    }

    /// <summary>
    /// A commit sha needs no sanitising and a repository with no commits has no sha to give, so this
    /// only has to survive whatever label the caller substitutes in that case.
    /// </summary>
    private static string Sanitise(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "head";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. label.Select(c => invalid.Contains(c) ? '_' : c)]);

        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }
}
