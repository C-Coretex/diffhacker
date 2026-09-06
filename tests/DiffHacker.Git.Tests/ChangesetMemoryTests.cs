using DiffHacker.Core.Changes;
using DiffHacker.TestSupport;

namespace DiffHacker.Git.Tests;

/// <summary>
/// Requirement 8: a large changeset must not be loaded into memory at once.
/// <para>
/// Verification item 9 asks for an assertion on peak allocation or on streaming behaviour rather
/// than on "it finished", because the naive implementation — capture stdout into a string, read
/// each file to describe it — also finishes. It just takes the whole diff with it.
/// </para>
/// </summary>
public sealed class ChangesetMemoryTests
{
    /// <summary>Comfortably bigger than the 5 MB content cap, so nothing may quietly hold it.</summary>
    private const long LargeFileBytes = 24L * 1024 * 1024;

    private const int SmallFiles = 300;

    [Fact]
    public async Task A_large_changeset_is_described_without_being_loaded()
    {
        var git = GitClientFactory.Create();
        using var fixture = BuildLargeChangeset();

        // Warm every code path first, so the measurement is the work and not the JIT.
        _ = await git.LoadAsync(fixture, TestContext.Current.CancellationToken);

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var changeset = await git.LoadAsync(fixture, TestContext.Current.CancellationToken);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        changeset.Files.Count.ShouldBe(SmallFiles + 1);

        // The point of the number: the changeset describes a 24 MB file, so an implementation
        // that buffered file content or buffered git's stdout would be far past this. Metadata
        // for three hundred files is not.
        allocated.ShouldBeLessThan(
            LargeFileBytes / 2,
            $"Describing a {LargeFileBytes / (1024 * 1024)} MB changeset allocated {allocated / (1024 * 1024)} MB, " +
            "which means something is holding content rather than streaming past it.");

        var large = changeset.File("huge/generated.txt");
        large.Status.ShouldBe(ChangeStatus.Added);
    }

    [Fact]
    public async Task A_file_past_the_cap_is_refused_rather_than_read()
    {
        var git = GitClientFactory.Create();
        using var fixture = BuildLargeChangeset();

        _ = await git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "huge/generated.txt", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        var before = GC.GetTotalAllocatedBytes(precise: true);

        var content = await git.GetFileContentAsync(
            new FileContentQuery(fixture.Root, "huge/generated.txt", FileSide.WorkingTree),
            TestContext.Current.CancellationToken);

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        content.Kind.ShouldBe(FileContentKind.TooLarge);
        content.SizeBytes.ShouldBeGreaterThanOrEqualTo(LargeFileBytes);

        // The size comes from a stat, not from reading the file, so refusing it costs nothing.
        allocated.ShouldBeLessThan(
            1024 * 1024,
            $"Refusing an oversized file allocated {allocated} bytes, so it was read before being refused.");
    }

    private static FixtureRepository BuildLargeChangeset()
    {
        var fixture = FixtureRepository.CreateWithCommit();

        for (var index = 0; index < SmallFiles; index++)
        {
            fixture.WriteFile($"src/module{index % 12}/file{index}.cs", $"// baseline {index}\n");
        }

        fixture.Git("add", "--all");
        fixture.Commit("baseline");

        for (var index = 0; index < SmallFiles; index++)
        {
            fixture.WriteFile($"src/module{index % 12}/file{index}.cs", $"// baseline {index}\n// edited\n");
        }

        fixture.WriteLargeTextFile("huge/generated.txt", LargeFileBytes);

        return fixture;
    }
}
