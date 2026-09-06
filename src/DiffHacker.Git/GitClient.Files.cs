using System.Globalization;
using System.Text;
using DiffHacker.Core.Changes;

namespace DiffHacker.Git;

/// <summary>
/// The on-demand half of <see cref="IGitClient"/>: one file's content, or one file's diff.
/// <para>
/// Everything here is bounded by <see cref="ContentLimits.MaxBytes"/> and reads forwards. That
/// is the other half of requirement 8 — the changeset stays metadata, and a reviewer opening a
/// file pays for that file and nothing else.
/// </para>
/// </summary>
public sealed partial class GitClient
{
    public async Task<FileContentResult> GetFileContentAsync(
        FileContentQuery query,
        CancellationToken cancellationToken)
    {
        var root = await RequireRepositoryAsync(query.RepositoryPath, cancellationToken).ConfigureAwait(false);
        var path = RequireRepositoryRelativePath(query.Path);

        return query.Side switch
        {
            FileSide.Head => await ReadHeadContentAsync(root, path, cancellationToken).ConfigureAwait(false),
            _ => await ReadWorkingTreeContentAsync(root, path, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<FileDiffResult> GetFileDiffAsync(FileDiffQuery query, CancellationToken cancellationToken)
    {
        var root = await RequireRepositoryAsync(query.RepositoryPath, cancellationToken).ConfigureAwait(false);
        var path = RequireRepositoryRelativePath(query.Path);
        var previousPath = query.PreviousPath is null ? null : RequireRepositoryRelativePath(query.PreviousPath);

        if (query.Untracked)
        {
            return await SynthesiseUntrackedDiffAsync(root, path, cancellationToken).ConfigureAwait(false);
        }

        var baseRevision = await ResolveBaseRevisionAsync(root, cancellationToken).ConfigureAwait(false);
        string[] pathspec = previousPath is null ? [path] : [previousPath, path];

        // Ask --numstat first. It is the machine-readable answer to "is this binary", and it
        // saves computing a patch that would only have said "Binary files differ" anyway.
        var isBinary = await IsBinaryAsync(root, baseRevision.Revision, pathspec, cancellationToken)
            .ConfigureAwait(false);

        if (isBinary is null)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.Absent,
                Path = path,
                PreviousPath = previousPath,
                SizeBytes = 0,
            };
        }

        if (isBinary.Value)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.Binary,
                Path = path,
                PreviousPath = previousPath,
                SizeBytes = WorkingTreeSize(root, path),
            };
        }

        using var captured = new CappedCapture();

        var outcome = await RunDiffAsync(
            root,
            [.. CommonDiffOptions, baseRevision.Revision, "--", .. pathspec],
            captured.ConsumeAsync,
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "diff");

        if (captured.TotalBytes > ContentLimits.MaxBytes)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.TooLarge,
                Path = path,
                PreviousPath = previousPath,
                SizeBytes = captured.TotalBytes,
            };
        }

        if (captured.TotalBytes == 0)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.Absent,
                Path = path,
                PreviousPath = previousPath,
                SizeBytes = 0,
            };
        }

        return new FileDiffResult
        {
            Kind = FileContentKind.Text,
            Path = path,
            PreviousPath = previousPath,
            SizeBytes = captured.TotalBytes,
            UnifiedDiff = TextDecoding.Decode(captured.Bytes, out _, out _),
        };
    }

    /// <summary>
    /// Whether git treats the file as binary, from <c>--numstat</c>'s dash rather than from
    /// anything said in prose.
    /// </summary>
    /// <returns>Null when the file did not change at all.</returns>
    private async Task<bool?> IsBinaryAsync(
        string root,
        string baseRevision,
        IReadOnlyList<string> pathspec,
        CancellationToken cancellationToken)
    {
        bool? binary = null;

        var outcome = await RunDiffAsync(
            root,
            ["--numstat", "-z", .. CommonDiffOptions, baseRevision, "--", .. pathspec],
            async (stream, token) =>
            {
                await foreach (var entry in GitOutputReaders.ReadNumstatAsync(stream, token).ConfigureAwait(false))
                {
                    binary ??= entry.LinesAdded is null;
                }
            },
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "diff --numstat");
        return binary;
    }

    /// <summary>
    /// Builds the diff for an untracked file, whose added side is the file itself
    /// (requirement 2).
    /// <para>
    /// Synthesised rather than obtained from <c>git diff --no-index /dev/null</c>: that spelling
    /// of the null device is not dependable across the three platforms this ships on, and the
    /// alternative would be writing a temporary file to compare against, which this layer has no
    /// business doing.
    /// </para>
    /// </summary>
    private static async Task<FileDiffResult> SynthesiseUntrackedDiffAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        var absolute = ToAbsolute(root, path);
        var facts = await UntrackedFileFacts.MeasureAsync(absolute, cancellationToken).ConfigureAwait(false);

        if (facts.IsBinary)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.Binary,
                Path = path,
                SizeBytes = facts.SizeBytes,
            };
        }

        if (facts.SizeBytes > ContentLimits.MaxBytes)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.TooLarge,
                Path = path,
                SizeBytes = facts.SizeBytes,
            };
        }

        var content = await ReadCappedFileAsync(absolute, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new FileDiffResult
            {
                Kind = FileContentKind.Absent,
                Path = path,
                SizeBytes = 0,
            };
        }

        var text = TextDecoding.Decode(content, out _, out _);
        var mode = facts.IsSymlink ? "120000" : "100644";

        var diff = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"diff --git a/{path} b/{path}\n")
            .Append(CultureInfo.InvariantCulture, $"new file mode {mode}\n");

        if (text.Length > 0)
        {
            var lines = SplitLines(text);

            diff.Append("--- /dev/null\n")
                .Append(CultureInfo.InvariantCulture, $"+++ b/{path}\n")
                .Append(CultureInfo.InvariantCulture, $"@@ -0,0 +1,{lines.Count} @@\n");

            foreach (var line in lines)
            {
                diff.Append('+').Append(line).Append('\n');
            }

            if (!text.EndsWith('\n'))
            {
                diff.Append("\\ No newline at end of file\n");
            }
        }

        return new FileDiffResult
        {
            Kind = FileContentKind.Text,
            Path = path,
            SizeBytes = facts.SizeBytes,
            UnifiedDiff = diff.ToString(),
        };
    }

    private async Task<FileContentResult> ReadHeadContentAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        var head = await runner
            .RunAsync("rev-parse", ["--verify", "--quiet", "HEAD"], root, cancellationToken)
            .ConfigureAwait(false);

        if (head.CouldNotRun)
        {
            throw Unavailable(head);
        }

        // No commits at all, so nothing has a HEAD side. Not an error — an absence.
        if (!head.Succeeded)
        {
            return FileContentResult.Absent();
        }

        var specification = "HEAD:" + path;

        var size = await runner
            .RunAsync("cat-file", ["-s", specification], root, cancellationToken)
            .ConfigureAwait(false);

        if (size.CouldNotRun)
        {
            throw Unavailable(size);
        }

        // git exits non-zero for a path that is not in HEAD, which is exactly the added-file case.
        if (!size.Succeeded ||
            !long.TryParse(size.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
        {
            return FileContentResult.Absent();
        }

        if (bytes > ContentLimits.MaxBytes)
        {
            return FileContentResult.TooLarge(bytes);
        }

        using var captured = new CappedCapture();

        var outcome = await runner.RunStreamingAsync(
            "cat-file",
            ["blob", specification],
            root,
            captured.ConsumeAsync,
            cancellationToken).ConfigureAwait(false);

        RequireSuccess(outcome, "cat-file blob");

        return Describe(captured.Bytes, captured.TotalBytes);
    }

    private static async Task<FileContentResult> ReadWorkingTreeContentAsync(
        string root,
        string path,
        CancellationToken cancellationToken)
    {
        var absolute = ToAbsolute(root, path);

        long size;
        try
        {
            var info = new FileInfo(absolute);
            if (!info.Exists)
            {
                // Deleted in the working tree. An absence, and the counterpart of the
                // added-file case above.
                return FileContentResult.Absent();
            }

            size = info.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileContentResult.Absent();
        }

        if (size > ContentLimits.MaxBytes)
        {
            return FileContentResult.TooLarge(size);
        }

        var bytes = await ReadCappedFileAsync(absolute, cancellationToken).ConfigureAwait(false);
        return bytes is null ? FileContentResult.Absent() : Describe(bytes, bytes.Length);
    }

    private static FileContentResult Describe(byte[] bytes, long totalBytes)
    {
        if (totalBytes > ContentLimits.MaxBytes)
        {
            return FileContentResult.TooLarge(totalBytes);
        }

        if (TextDecoding.LooksBinary(bytes))
        {
            return FileContentResult.Binary(totalBytes);
        }

        var text = TextDecoding.Decode(bytes, out var encoding, out var usedFallback);

        return new FileContentResult
        {
            Kind = FileContentKind.Text,
            Text = text,
            SizeBytes = totalBytes,
            Encoding = encoding,
            UsedFallbackEncoding = usedFallback,
        };
    }

    private static async Task<byte[]?> ReadCappedFileAsync(string absolutePath, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long WorkingTreeSize(string root, string path)
    {
        try
        {
            var info = new FileInfo(ToAbsolute(root, path));
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Splits on newlines, keeping <c>\r</c> so a CRLF file round-trips through the diff.</summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                lines.Add(text[start..]);
                break;
            }

            lines.Add(text[start..newline]);
            start = newline + 1;
        }

        return lines;
    }

    private static string ToAbsolute(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Rejects anything that is not a plain repository-relative path.
    /// <para>
    /// Iteration 5 sandboxes the toolbox properly; this is the narrower promise that the Git
    /// layer itself never reaches outside the repository it was given, so that the seam is
    /// closed before anything is built on top of it.
    /// </para>
    /// </summary>
    private static string RequireRepositoryRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalised = path.Replace('\\', '/');

        if (Path.IsPathRooted(normalised) || normalised.StartsWith('/'))
        {
            throw new GitClientException(
                $"'{path}' is not a repository-relative path.",
                GitClientFailure.RepositoryUnreadable);
        }

        foreach (var segment in normalised.Split('/'))
        {
            if (segment is ".." or ".")
            {
                throw new GitClientException(
                    $"'{path}' is not a repository-relative path.",
                    GitClientFailure.RepositoryUnreadable);
            }
        }

        return normalised;
    }

    /// <summary>
    /// Reads a stream, keeping at most <see cref="ContentLimits.MaxBytes"/> but counting all of
    /// it — so "this diff is 41 MB" is a true statement rather than "at least 5 MB".
    /// </summary>
    private sealed class CappedCapture : IDisposable
    {
        private readonly MemoryStream _kept = new();

        public long TotalBytes { get; private set; }

        public byte[] Bytes => _kept.ToArray();

        public void Dispose() => _kept.Dispose();

        public async Task ConsumeAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];

            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                TotalBytes += read;

                var room = ContentLimits.MaxBytes - _kept.Length;
                if (room > 0)
                {
                    _kept.Write(buffer, 0, (int)Math.Min(room, read));
                }
            }
        }
    }
}
