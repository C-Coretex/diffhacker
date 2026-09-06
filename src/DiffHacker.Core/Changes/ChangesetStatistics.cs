namespace DiffHacker.Core.Changes;

/// <summary>
/// Deterministic totals for a changeset, computed by the application rather than the LLM.
/// <para>
/// These are the numbers Iteration 7 puts in the prompt and Iteration 13 reports cost against,
/// so they are counted from <see cref="Changeset.Files"/> and never estimated.
/// </para>
/// </summary>
public sealed record ChangesetStatistics
{
    public required int TotalFiles { get; init; }

    /// <summary>Sum of countable added lines. Binaries and submodules contribute nothing.</summary>
    public required int TotalLinesAdded { get; init; }

    /// <inheritdoc cref="TotalLinesAdded"/>
    public required int TotalLinesRemoved { get; init; }

    public required ChangesetStatusCounts ByStatus { get; init; }

    /// <summary>Files whose line counts are unknowable, so the line totals under-report them.</summary>
    public required int BinaryFiles { get; init; }

    public required int SubmoduleFiles { get; init; }

    public required int UntrackedFiles { get; init; }

    /// <summary>Distinct detected languages, sorted. Files with no detected language are absent.</summary>
    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>Distinct project names, sorted.</summary>
    public required IReadOnlyList<string> Projects { get; init; }

    public static ChangesetStatistics From(IReadOnlyList<ChangedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var counts = new ChangesetStatusCounts
        {
            Added = files.Count(file => file.Status is ChangeStatus.Added),
            Modified = files.Count(file => file.Status is ChangeStatus.Modified),
            Deleted = files.Count(file => file.Status is ChangeStatus.Deleted),
            Renamed = files.Count(file => file.Status is ChangeStatus.Renamed),
            Copied = files.Count(file => file.Status is ChangeStatus.Copied),
        };

        return new ChangesetStatistics
        {
            TotalFiles = files.Count,
            TotalLinesAdded = files.Sum(file => file.LinesAdded ?? 0),
            TotalLinesRemoved = files.Sum(file => file.LinesRemoved ?? 0),
            ByStatus = counts,
            BinaryFiles = files.Count(file => file.IsBinary),
            SubmoduleFiles = files.Count(file => file.IsSubmodule),
            UntrackedFiles = files.Count(file => file.IsUntracked),
            Languages = files
                .Select(file => file.Language)
                .Where(language => !string.IsNullOrEmpty(language))
                .Select(language => language!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Projects = files
                .Select(file => file.Project.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
    }
}

/// <summary>How many files landed in each status.</summary>
public sealed record ChangesetStatusCounts
{
    public required int Added { get; init; }

    public required int Modified { get; init; }

    public required int Deleted { get; init; }

    public required int Renamed { get; init; }

    public required int Copied { get; init; }
}
