using System.Globalization;
using DiffHacker.Core.Changes;

namespace DiffHacker.Git;

/// <summary>
/// One record of <c>git diff --raw -z</c>: the authoritative statement of what changed.
/// <para>
/// This format, not the patch header, is where paths, statuses and file modes come from. The
/// modes are the reason: <c>120000</c> is a symlink and <c>160000</c> is a submodule, so the two
/// awkward cases requirement 7 names are answered by a field git already hands over rather than
/// by probing the filesystem and hoping.
/// </para>
/// </summary>
internal sealed record GitRawEntry
{
    public required string SourceMode { get; init; }

    public required string DestinationMode { get; init; }

    public required string SourceOid { get; init; }

    public required string DestinationOid { get; init; }

    /// <summary>The raw status letter, before it is mapped onto <see cref="ChangeStatus"/>.</summary>
    public required char StatusLetter { get; init; }

    public required string Path { get; init; }

    /// <summary>Populated for <c>R</c> and <c>C</c> only.</summary>
    public string? PreviousPath { get; init; }

    public bool IsSubmodule =>
        string.Equals(SourceMode, SubmoduleMode, StringComparison.Ordinal) ||
        string.Equals(DestinationMode, SubmoduleMode, StringComparison.Ordinal);

    public bool IsSymlink =>
        string.Equals(SourceMode, SymlinkMode, StringComparison.Ordinal) ||
        string.Equals(DestinationMode, SymlinkMode, StringComparison.Ordinal);

    private const string SubmoduleMode = "160000";
    private const string SymlinkMode = "120000";
    private const string AbsentMode = "000000";

    public ChangeStatus Status => StatusLetter switch
    {
        'A' => ChangeStatus.Added,
        'D' => ChangeStatus.Deleted,
        'R' => ChangeStatus.Renamed,
        'C' => ChangeStatus.Copied,

        // 'M' modified, 'T' type changed, 'U' unmerged, 'X' git's own "should not happen".
        // All of them are, to a reviewer, a file that is different now.
        _ => ChangeStatus.Modified,
    };

    public string? SubmoduleFromCommit =>
        IsSubmodule && !string.Equals(SourceOid, EmptyOid(SourceOid.Length), StringComparison.Ordinal)
            ? SourceOid
            : null;

    public string? SubmoduleToCommit =>
        IsSubmodule && !string.Equals(DestinationOid, EmptyOid(DestinationOid.Length), StringComparison.Ordinal)
            ? DestinationOid
            : null;

    /// <summary>True when the file does not exist on the working-tree side.</summary>
    public bool DestinationAbsent => string.Equals(DestinationMode, AbsentMode, StringComparison.Ordinal);

    private static string EmptyOid(int length) => new('0', length);

    /// <summary>
    /// Parses one metadata field, the part before the first NUL.
    /// <para>
    /// Shape: <c>:&lt;src mode&gt; &lt;dst mode&gt; &lt;src oid&gt; &lt;dst oid&gt; &lt;status&gt;</c>.
    /// A conflicted file mid-merge produces a combined record with one extra colon and one extra
    /// mode/oid pair per parent, so the parser counts colons instead of assuming one.
    /// </para>
    /// </summary>
    public static bool TryParseMetadata(
        string metadata,
        out string sourceMode,
        out string destinationMode,
        out string sourceOid,
        out string destinationOid,
        out char statusLetter,
        out int score)
    {
        sourceMode = destinationMode = sourceOid = destinationOid = string.Empty;
        statusLetter = '\0';
        score = 0;

        var parents = 0;
        while (parents < metadata.Length && metadata[parents] == ':')
        {
            parents++;
        }

        if (parents == 0)
        {
            return false;
        }

        var fields = metadata[parents..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // parents + 1 modes, then parents + 1 oids, then the status token.
        var expected = ((parents + 1) * 2) + 1;
        if (fields.Length < expected)
        {
            return false;
        }

        // For a combined record the interesting sides are the first parent and the destination,
        // which is exactly what a reviewer comparing against HEAD wants to see.
        sourceMode = fields[0];
        destinationMode = fields[parents];
        sourceOid = fields[parents + 1];
        destinationOid = fields[(parents * 2) + 1];

        var status = fields[expected - 1];
        if (status.Length == 0)
        {
            return false;
        }

        statusLetter = char.ToUpperInvariant(status[0]);

        if (status.Length > 1 &&
            int.TryParse(status.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            score = parsed;
        }

        return true;
    }

    /// <summary>Whether this status letter is followed by two paths rather than one.</summary>
    public static bool HasTwoPaths(char statusLetter) => statusLetter is 'R' or 'C';
}
