using System.Globalization;
using System.Text;

namespace DiffHacker.Git;

/// <summary>Line counts for one file, as <c>git diff --numstat</c> reports them.</summary>
/// <param name="LinesAdded">Null when git reported <c>-</c>, meaning it will not count a binary.</param>
/// <param name="LinesRemoved">Null for the same reason.</param>
/// <param name="Path">The post-image path, which is what the raw record also keys on.</param>
internal readonly record struct GitNumstatEntry(int? LinesAdded, int? LinesRemoved, string Path);

/// <summary>
/// Streaming readers for the three <c>git diff</c> output formats this layer consumes.
/// <para>
/// All three read forwards through the stream and hold at most one record at a time, which is
/// how requirement 8 is satisfied for a changeset of any size: memory is proportional to the
/// longest single path, not to the size of the diff.
/// </para>
/// </summary>
internal static class GitOutputReaders
{
    /// <summary>
    /// Reads <c>git diff --raw -z</c>: a metadata field, then one path, or two for a rename or
    /// a copy.
    /// </summary>
    public static async IAsyncEnumerable<GitRawEntry> ReadRawAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = new NulFieldReader(stream);

        while (true)
        {
            var metadata = await reader.ReadFieldAsync(cancellationToken).ConfigureAwait(false);
            if (metadata is null)
            {
                yield break;
            }

            if (!GitRawEntry.TryParseMetadata(
                    metadata,
                    out var sourceMode,
                    out var destinationMode,
                    out var sourceOid,
                    out var destinationOid,
                    out var statusLetter,
                    out _))
            {
                // Not a record we understand. Stopping is safer than resynchronising blindly:
                // the caller treats a short read as "this pass failed" rather than as a shorter
                // changeset, so no file is ever silently dropped (§0.2.5).
                throw new FormatException($"Unrecognised git diff --raw record: '{metadata}'.");
            }

            var first = await reader.ReadFieldAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new FormatException("git diff --raw ended before the path of its last record.");

            string path;
            string? previousPath = null;

            if (GitRawEntry.HasTwoPaths(statusLetter))
            {
                previousPath = first;
                path = await reader.ReadFieldAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new FormatException("git diff --raw ended before the second path of a rename.");
            }
            else
            {
                path = first;
            }

            yield return new GitRawEntry
            {
                SourceMode = sourceMode,
                DestinationMode = destinationMode,
                SourceOid = sourceOid,
                DestinationOid = destinationOid,
                StatusLetter = statusLetter,
                Path = path,
                PreviousPath = previousPath,
            };
        }
    }

    /// <summary>
    /// Reads <c>git diff --numstat -z</c>.
    /// <para>
    /// Two shapes, and missing the second is the classic way to lose renamed files:
    /// <c>added TAB removed TAB path NUL</c> normally, but
    /// <c>added TAB removed TAB NUL from NUL to NUL</c> for a rename or a copy, where the path
    /// inside the first field is empty and two more fields follow.
    /// </para>
    /// </summary>
    public static async IAsyncEnumerable<GitNumstatEntry> ReadNumstatAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = new NulFieldReader(stream);

        while (true)
        {
            var record = await reader.ReadFieldAsync(cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                yield break;
            }

            var parts = record.Split('\t');
            if (parts.Length < 3)
            {
                throw new FormatException($"Unrecognised git diff --numstat record: '{record}'.");
            }

            var path = parts[2];
            if (path.Length == 0)
            {
                // Rename or copy: skip the pre-image path, keep the post-image path.
                _ = await reader.ReadFieldAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new FormatException("git diff --numstat ended before a rename's source path.");

                path = await reader.ReadFieldAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new FormatException("git diff --numstat ended before a rename's target path.");
            }

            yield return new GitNumstatEntry(ParseCount(parts[0]), ParseCount(parts[1]), path);
        }
    }

    /// <summary>A dash means "binary, not counted" — which is a different claim from zero.</summary>
    private static int? ParseCount(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}

/// <summary>
/// Counts hunks per file section in a <c>-U0</c> patch stream.
/// <para>
/// Counting rather than parsing, deliberately. Patch headers spell paths through
/// <c>core.quotePath</c>, <c>diff.mnemonicPrefix</c> and quoting rules that CLAUDE.md forbids
/// this application from relying on, so nothing here reads a path: it counts
/// <c>diff --git</c> section boundaries and <c>@@</c> headers, and the caller zips the result
/// against the authoritative <c>--raw</c> list by position. If the two disagree the caller
/// reports no hunk counts at all rather than attributing numbers to the wrong files.
/// </para>
/// <para>
/// Memory is a fixed sixteen bytes regardless of the patch: only the start of each line is kept,
/// which matters because one minified file is one line several megabytes long.
/// </para>
/// </summary>
internal sealed class PatchHunkScanner
{
    private const int PrefixBytes = 16;

    private static readonly byte[] SectionMarker = Encoding.ASCII.GetBytes("diff --git ");
    private static readonly byte[] HunkMarker = Encoding.ASCII.GetBytes("@@ ");

    private readonly byte[] _prefix = new byte[PrefixBytes];
    private readonly List<int> _sections = [];
    private int _prefixLength;
    private bool _lineStarted;

    /// <summary>Hunks per file, in the order the patch emitted them.</summary>
    public IReadOnlyList<int> Sections => _sections;

    public void Feed(ReadOnlySpan<byte> chunk)
    {
        foreach (var value in chunk)
        {
            if (value == (byte)'\n')
            {
                EndLine();
                continue;
            }

            _lineStarted = true;
            if (_prefixLength < PrefixBytes)
            {
                _prefix[_prefixLength++] = value;
            }
        }
    }

    /// <summary>Closes a final line that had no trailing newline.</summary>
    public void Complete()
    {
        if (_lineStarted)
        {
            EndLine();
        }
    }

    private void EndLine()
    {
        var line = _prefix.AsSpan(0, _prefixLength);

        if (line.StartsWith(SectionMarker))
        {
            _sections.Add(0);
        }
        else if (_sections.Count > 0 && line.StartsWith(HunkMarker))
        {
            _sections[^1]++;
        }

        _prefixLength = 0;
        _lineStarted = false;
    }
}
