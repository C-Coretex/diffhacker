using System.Runtime.Serialization;
using DiffHacker.Contracts;

namespace DiffHacker.Contracts.Tests;

/// <summary>
/// The changeset wire enums, pinned.
/// <para>
/// <c>FileDiffInfo</c> and <c>FileContentInfo</c> describe the same four outcomes — text,
/// binary, absent, too large — and JSON Schema cannot share a definition across files without
/// the generator duplicating the type into both outputs. Two copies are tolerable only while
/// something checks they cannot drift apart, which is this.
/// </para>
/// </summary>
public sealed class ChangesetEnumAgreementTests
{
    private static readonly string[] ExpectedKinds = ["text", "binary", "absent", "too_large"];

    [Fact]
    public void The_change_statuses_are_the_five_the_iteration_specifies()
    {
        // Iteration 3 requirement 3. A type change and an unmerged file map onto modified rather
        // than adding vocabulary the renderer and the LLM would both have to learn.
        WireValues<ChangedFileInfoStatus>()
            .ShouldBe(["added", "modified", "deleted", "renamed", "copied"], ignoreOrder: true);
    }

    [Fact]
    public void The_diff_kinds_cover_every_outcome_a_reviewer_can_hit()
    {
        WireValues<FileDiffInfoKind>().ShouldBe(ExpectedKinds, ignoreOrder: true);
    }

    [Fact]
    public void The_content_kinds_agree_with_the_diff_kinds()
    {
        // Iteration 10 switches on these once. If the two lists diverged, one of its branches
        // would silently stop being reachable.
        WireValues<FileContentInfoKind>().ShouldBe(WireValues<FileDiffInfoKind>(), ignoreOrder: true);
    }

    [Fact]
    public void The_two_sides_of_a_comparison_are_head_and_working_tree()
    {
        WireValues<FileContentRequestSide>().ShouldBe(["head", "working_tree"], ignoreOrder: true);
    }

    private static string[] WireValues<TEnum>()
        where TEnum : struct, Enum =>
        [.. typeof(TEnum)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => field.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                .Cast<EnumMemberAttribute>()
                .FirstOrDefault()?.Value ?? field.Name)];
}
