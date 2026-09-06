using DiffHacker.Core.Changes;

namespace DiffHacker.Git;

/// <summary>
/// What can be said about an untracked file without git's help.
/// <para>
/// Git will not diff a file it does not know about, so its line count and its binary flag have
/// to come from the file itself. The alternative — one <c>git diff --no-index</c> process per
/// untracked file — costs a process launch each on a change that added three hundred files, and
/// the alternatives that avoid that all write to the object database.
/// </para>
/// <para>
/// The binary test is git's own: a NUL byte in the first
/// <see cref="ContentLimits.BinarySniffBytes"/>. Matching it matters, because a file this layer
/// calls text and git calls binary would show line counts nothing else agrees with.
/// </para>
/// </summary>
internal static class UntrackedFileFacts
{
    /// <param name="SizeBytes">Size on disk.</param>
    /// <param name="Lines">
    /// Newline-terminated line count, plus one for a trailing partial line. Null when the file is
    /// binary, unreadable, or too large to be worth walking.
    /// </param>
    /// <param name="IsBinary">True when git would refuse to diff it as text.</param>
    /// <param name="IsSymlink">True when the entry is a symbolic link or other reparse point.</param>
    internal readonly record struct Facts(long SizeBytes, int? Lines, bool IsBinary, bool IsSymlink);

    public static async Task<Facts> MeasureAsync(string absolutePath, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                // Listed by git a moment ago and gone now. The file still belongs in the
                // changeset; there is simply nothing left to measure.
                return new Facts(0, null, false, false);
            }

            var isSymlink = info.LinkTarget is not null;
            var size = info.Length;

            if (size == 0)
            {
                return new Facts(0, 0, false, isSymlink);
            }

            await using var stream = new FileStream(
                absolutePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });

            var buffer = new byte[64 * 1024];
            var first = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (TextDecoding.LooksBinary(buffer.AsSpan(0, first)))
            {
                return new Facts(size, null, true, isSymlink);
            }

            // Past the cap the file is not going to be shown as text anyway, so walking it to
            // count lines buys a number nobody will see.
            if (size > ContentLimits.MaxBytes)
            {
                return new Facts(size, null, false, isSymlink);
            }

            var lines = 0;
            var lastByte = (byte)0;
            var read = first;

            while (read > 0)
            {
                var span = buffer.AsSpan(0, read);
                lines += span.Count((byte)'\n');
                lastByte = span[^1];

                read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            // A file with no trailing newline still ends in a line, and git counts it as one
            // (requirement 7's "files with no trailing newline").
            if (lastByte != (byte)'\n')
            {
                lines++;
            }

            return new Facts(size, lines, false, isSymlink);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A dangling symlink, a locked file, a permission we do not have. The file is still
            // part of the change and still appears; its numbers are simply unknown.
            return new Facts(0, null, false, false);
        }
    }
}
