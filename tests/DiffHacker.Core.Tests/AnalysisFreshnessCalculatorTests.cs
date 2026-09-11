using DiffHacker.Core.Analyses;
using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// Whether a stored analysis still describes the working tree. Every case starts from a changeset
/// identical to the one analysed — fresh — and moves exactly one thing.
/// </summary>
public sealed class AnalysisFreshnessCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1);

    private const string Head = "head0001";

    [Fact]
    public void The_changeset_that_was_analysed_is_fresh_and_compared_by_content()
    {
        var report = Evaluate(Hashed(AnalysisFixtures.Changeset()));

        report.IsStale.ShouldBeFalse();
        report.Basis.ShouldBe(AnalysisFreshnessBasis.Content);
        report.Modified.ShouldBeEmpty();
        report.Added.ShouldBeEmpty();
        report.Removed.ShouldBeEmpty();
        report.HeadMoved.ShouldBeFalse();
        report.CheckedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void An_edit_is_stale_even_when_it_keeps_the_line_counts()
    {
        var now = Hashed(AnalysisFixtures.Changeset())
            .Select(file => file.Path == AnalysisFixtures.ContractPath
                ? file with { ContentSha256 = "edited" }
                : file)
            .ToList();

        var report = Evaluate(now);

        report.IsStale.ShouldBeTrue();
        report.Modified.ShouldBe([AnalysisFixtures.ContractPath]);
    }

    [Fact]
    public void Reverting_an_edit_makes_the_analysis_fresh_again()
    {
        // What a modification time could not do: the file was touched twice and is byte for byte
        // what was analysed. The calculator keeps no memory between checks, so each answer is about
        // the tree as it is now.
        var edited = Hashed(AnalysisFixtures.Changeset())
            .Select(file => file.Path == AnalysisFixtures.CallerPath ? file with { ContentSha256 = "edited" } : file)
            .ToList();

        Evaluate(edited).IsStale.ShouldBeTrue();
        Evaluate(Hashed(AnalysisFixtures.Changeset())).IsStale.ShouldBeFalse();
    }

    [Fact]
    public void A_file_that_joined_the_change_is_added_and_one_that_left_it_is_removed()
    {
        var now = Hashed(AnalysisFixtures.Changeset())
            .Where(file => file.Path != AnalysisFixtures.CallerPath)
            .Append(AnalysisFixtures.File("src/New.cs", ChangeStatus.Added) with { ContentSha256 = "new" })
            .ToList();

        var report = Evaluate(now);

        report.IsStale.ShouldBeTrue();
        report.Added.ShouldBe(["src/New.cs"]);
        report.Removed.ShouldBe([AnalysisFixtures.CallerPath]);
        report.Modified.ShouldBeEmpty();
    }

    [Fact]
    public void A_change_of_status_is_a_modification_whatever_the_content()
    {
        var now = Hashed(AnalysisFixtures.Changeset())
            .Select(file => file.Path == AnalysisFixtures.ContractPath
                ? file with { Status = ChangeStatus.Added }
                : file)
            .ToList();

        Evaluate(now).Modified.ShouldBe([AnalysisFixtures.ContractPath]);
    }

    [Fact]
    public void Head_moving_is_stale_on_its_own()
    {
        // The committed side of every diff moved, so every explanation may be describing a different
        // change even though the working tree did not.
        var report = Evaluate(Hashed(AnalysisFixtures.Changeset()), currentHead: "head0002");

        report.IsStale.ShouldBeTrue();
        report.HeadMoved.ShouldBeTrue();
        report.RecordedHeadCommit.ShouldBe(Head);
        report.CurrentHeadCommit.ShouldBe("head0002");
        report.Modified.ShouldBeEmpty();
    }

    [Fact]
    public void An_analysis_without_hashes_is_compared_by_line_counts_and_says_so()
    {
        // Written by a build that predates the fingerprint.
        var stored = Stored(AnalysisFixtures.Changeset());

        Evaluate(Hashed(AnalysisFixtures.Changeset()), stored).Basis.ShouldBe(AnalysisFreshnessBasis.LineCounts);

        var moved = Hashed(AnalysisFixtures.Changeset())
            .Select(file => file.Path == AnalysisFixtures.CallerPath ? file with { LinesAdded = 5 } : file)
            .ToList();

        var report = Evaluate(moved, stored);

        report.IsStale.ShouldBeTrue();
        report.Modified.ShouldBe([AnalysisFixtures.CallerPath]);
    }

    [Fact]
    public void A_deleted_file_is_not_a_weaker_comparison_for_having_no_hash()
    {
        // Nothing to hash on either day. Its status and counts say everything, so it must not drag
        // the basis down to line counts on its own.
        var report = Evaluate(Hashed(AnalysisFixtures.Changeset()));

        report.Basis.ShouldBe(AnalysisFreshnessBasis.Content);
    }

    [Fact]
    public void An_analysis_with_no_per_file_record_can_only_compare_head()
    {
        var stored = Stored([]) with { ChangedFiles = [] };

        var fresh = AnalysisFreshnessCalculator.HeadOnly(stored, Head, Now);

        fresh.Basis.ShouldBe(AnalysisFreshnessBasis.HeadOnly);
        fresh.IsStale.ShouldBeFalse();

        AnalysisFreshnessCalculator.HeadOnly(stored, "head0002", Now).IsStale.ShouldBeTrue();

        Evaluate(Hashed(AnalysisFixtures.Changeset()), stored).Basis.ShouldBe(AnalysisFreshnessBasis.HeadOnly);
    }

    [Fact]
    public void Every_list_is_sorted()
    {
        var now = new List<ChangedFile>(Hashed(AnalysisFixtures.Changeset()))
        {
            AnalysisFixtures.File("z.cs", ChangeStatus.Added),
            AnalysisFixtures.File("a.cs", ChangeStatus.Added),
            AnalysisFixtures.File("m.cs", ChangeStatus.Added),
        };

        Evaluate(now).Added.ShouldBe(["a.cs", "m.cs", "z.cs"]);
    }

    /// <summary>The fixture changeset as a run would have read it: every file with a working tree hashed.</summary>
    private static List<ChangedFile> Hashed(IEnumerable<ChangedFile> files) =>
    [
        .. files.Select(static file => file with
        {
            ContentSha256 = file.Status is ChangeStatus.Deleted ? null : "sha:" + file.Path,
        }),
    ];

    private static AnalysisFreshnessReport Evaluate(
        List<ChangedFile> now,
        Analysis? stored = null,
        string? currentHead = Head) =>
        AnalysisFreshnessCalculator.Evaluate(
            stored ?? Stored(Hashed(AnalysisFixtures.Changeset())),
            new Changeset
            {
                RepositoryPath = "/repo",
                IsClean = now.Count == 0,
                HasCommits = true,
                UntrackedIncluded = true,
                Files = now,
                Statistics = ChangesetStatistics.From(now),
                HunkCountsAvailable = true,
            },
            currentHead,
            Now);

    private static Analysis Stored(IEnumerable<ChangedFile> analysed)
    {
        var document = AnalysisFixtures.Valid();
        var files = analysed.ToList();

        return new Analysis
        {
            Id = "analysis1",
            RepositoryPath = "/repo",
            SchemaVersion = Contracts.ContractVersion.Current,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            HeadCommit = Head,
            ProviderDisplayName = "Test",
            Model = "gpt-4o",
            Document = document,
            Statistics = AnalysisStatistics.From(
                document,
                AnalysisGrouping.DependencyFlow,
                ChangesetStatistics.From(files),
                AnalysisGraph.Build(document.Nodes, document.Edges)),
            ChangedFiles = [.. files.Select(ChangedFileFacts.From)],
        };
    }
}
