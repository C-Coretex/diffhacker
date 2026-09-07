using DiffHacker.Core.Knowledge;

namespace DiffHacker.Core.Tests;

/// <summary>
/// What a stored profile turns into: the text a model reads, the size it is charged for, and the
/// documentation it can be rendered into.
/// </summary>
public sealed class ProfileTextRendererTests
{
    [Fact]
    public void The_rendered_document_names_the_repository_rather_than_labelling_its_sections()
    {
        var text = ProfileTextRenderer.RenderDocument(Sample.Document());

        text.ShouldContain("A desktop reviewer for large diffs");
        text.ShouldContain("DiffHacker.Core");
        text.ShouldContain("src/DiffHacker.Core");
        text.ShouldContain("Related: DiffHacker.Git");
    }

    [Fact]
    public void An_empty_section_is_omitted_rather_than_rendered_as_a_bare_heading()
    {
        var document = Sample.Document() with { TestLayout = "   " };

        ProfileTextRenderer.RenderDocument(document).ShouldNotContain("## Tests");
    }

    [Fact]
    public void The_users_words_come_last_and_are_labelled_as_theirs()
    {
        var text = ProfileTextRenderer.Render(Sample.Profile() with
        {
            UserNotes = "The Legacy project is being deleted.",
            CustomInstructions = "Ignore the generated/ folder.",
        });

        text.ShouldNotBeNull();
        text.ShouldContain("The Legacy project is being deleted.");
        text.ShouldContain("Ignore the generated/ folder.");

        // Ordering is load-bearing: a model reading a correction after the thing it corrects
        // treats it as a correction, which is what the user meant by writing it.
        text.IndexOf("## Purpose", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("Notes from the reviewer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_profile_with_nothing_in_it_renders_as_nothing_rather_than_as_empty_headings()
    {
        var empty = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch);

        ProfileTextRenderer.Render(empty).ShouldBeNull();
    }

    [Fact]
    public void Custom_instructions_alone_are_worth_rendering_before_any_profile_exists()
    {
        var profile = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch) with
        {
            CustomInstructions = "This is CQRS.",
        };

        ProfileTextRenderer.Render(profile).ShouldNotBeNull().ShouldContain("This is CQRS.");
    }

    [Fact]
    public void The_provenance_line_says_when_and_from_which_commit()
    {
        var text = ProfileTextRenderer.Render(Sample.Profile()).ShouldNotBeNull();

        text.ShouldContain("2026-05-01");
        text.ShouldContain("abc12345");
    }
}

public sealed class ProfileBudgetTests
{
    [Fact]
    public void The_budget_measures_the_rendered_text_not_the_json_around_it()
    {
        var document = Sample.Document();

        ProfileBudget.Measure(document).ShouldBe(ProfileTextRenderer.RenderDocument(document).Length);
    }

    [Fact]
    public void A_missing_budget_is_the_default()
    {
        ProfileBudget.Clamp(null).ShouldBe(ProfileBudget.DefaultCharacters);
    }

    [Fact]
    public void A_budget_outside_the_supported_range_is_clamped_rather_than_refused()
    {
        ProfileBudget.Clamp(10).ShouldBe(ProfileBudget.MinimumCharacters);
        ProfileBudget.Clamp(10_000_000).ShouldBe(ProfileBudget.MaximumCharacters);
        ProfileBudget.Clamp(20_000).ShouldBe(20_000);
    }

    [Fact]
    public void The_users_own_sections_are_measured_but_are_not_part_of_the_budget()
    {
        var profile = Sample.Profile() with { UserNotes = "abc", CustomInstructions = "de" };

        ProfileTextRenderer.MeasureUserSections(profile).ShouldBe(5);
        ProfileBudget.Measure(profile.Document!).ShouldBe(ProfileBudget.Measure(Sample.Document()));
    }
}

public sealed class ProfileDriftCalculatorTests
{
    [Fact]
    public void A_repository_that_has_barely_moved_has_not_drifted()
    {
        var drift = ProfileDriftCalculator.Evaluate(filesChanged: 4, trackedFiles: 900, commitReachable: true);

        drift.IsSubstantial.ShouldBeFalse();
        drift.FilesChanged.ShouldBe(4);
        drift.TrackedFiles.ShouldBe(900);
    }

    [Fact]
    public void More_than_a_tenth_of_a_small_repository_is_substantial()
    {
        // Fifty files out of two hundred is most of a small project, and well under the absolute
        // threshold — the proportional rule is what catches it.
        ProfileDriftCalculator
            .Evaluate(filesChanged: 50, trackedFiles: 200, commitReachable: true)
            .IsSubstantial.ShouldBeTrue();
    }

    [Fact]
    public void More_than_a_hundred_and_fifty_files_is_substantial_however_large_the_repository()
    {
        ProfileDriftCalculator
            .Evaluate(filesChanged: 200, trackedFiles: 100_000, commitReachable: true)
            .IsSubstantial.ShouldBeTrue();
    }

    [Fact]
    public void A_commit_that_is_no_longer_in_the_repository_is_substantial_drift_by_definition()
    {
        var drift = ProfileDriftCalculator.Evaluate(filesChanged: 0, trackedFiles: 10, commitReachable: false);

        drift.IsSubstantial.ShouldBeTrue();
        drift.CommitReachable.ShouldBe(false);

        // No number is reported, because there is none: saying "0 files changed" about a commit
        // that cannot be found would be a confident lie.
        drift.FilesChanged.ShouldBeNull();
    }

    [Fact]
    public void An_unmeasured_repository_reports_nothing_and_does_not_nag()
    {
        ProfileDrift.Unknown.IsSubstantial.ShouldBeFalse();
        ProfileDrift.Unknown.CommitReachable.ShouldBeNull();
    }
}

public sealed class DocumentationRendererTests
{
    [Fact]
    public void Three_files_are_produced_in_a_stable_order()
    {
        var documents = DocumentationRenderer.Render(Sample.Profile(), DocumentationTarget.DocsDirectory);

        documents.Select(document => document.RelativePath).ShouldBe(
            ["docs/ARCHITECTURE.md", "docs/MODULES.md", "docs/CONVENTIONS.md"]);
    }

    [Fact]
    public void The_repository_root_target_drops_the_docs_prefix()
    {
        var documents = DocumentationRenderer.Render(Sample.Profile(), DocumentationTarget.RepositoryRoot);

        documents.Select(document => document.RelativePath).ShouldBe(
            ["ARCHITECTURE.md", "MODULES.md", "CONVENTIONS.md"]);
    }

    [Fact]
    public void The_documents_link_to_each_other()
    {
        var documents = DocumentationRenderer.Render(Sample.Profile(), DocumentationTarget.RepositoryRoot);

        documents[0].Content.ShouldContain("[MODULES.md](MODULES.md)");
        documents[1].Content.ShouldContain("[ARCHITECTURE.md](ARCHITECTURE.md)");
        documents[2].Content.ShouldContain("[MODULES.md](MODULES.md)");
    }

    [Fact]
    public void Every_document_says_it_was_generated_and_from_which_commit()
    {
        foreach (var document in DocumentationRenderer.Render(Sample.Profile(), DocumentationTarget.RepositoryRoot))
        {
            document.Content.ShouldContain("Generated by DiffHacker");
            document.Content.ShouldContain("abc12345");
        }
    }

    [Fact]
    public void The_module_map_carries_every_module_as_a_row_and_a_section()
    {
        var modules = DocumentationRenderer
            .Render(Sample.Profile(), DocumentationTarget.RepositoryRoot)[1]
            .Content;

        modules.ShouldContain("| [DiffHacker.Core](#diffhackercore) |");
        modules.ShouldContain("## DiffHacker.Core");
        modules.ShouldContain("## DiffHacker.Git");
    }

    [Fact]
    public void A_summary_containing_a_pipe_does_not_break_the_table()
    {
        var profile = Sample.Profile() with
        {
            Document = Sample.Document() with
            {
                Modules = [new ProjectModule { Name = "A", Path = "a", Summary = "reads a|b" }],
            },
        };

        DocumentationRenderer.Render(profile, DocumentationTarget.RepositoryRoot)[1]
            .Content.ShouldContain(@"reads a\|b");
    }

    [Fact]
    public void Rendering_without_a_generated_profile_is_refused_rather_than_invented()
    {
        var empty = ProjectProfile.Empty("/repo", DateTimeOffset.UnixEpoch);

        Should.Throw<InvalidOperationException>(
            () => DocumentationRenderer.Render(empty, DocumentationTarget.RepositoryRoot));
    }

    [Fact]
    public void The_same_profile_renders_the_same_bytes_every_time()
    {
        // The export gate hashes this output, so a renderer that varied would refuse every write.
        var first = DocumentationRenderer.Render(Sample.Profile(), DocumentationTarget.DocsDirectory);
        var second = DocumentationRenderer.Render(Sample.Profile(), DocumentationTarget.DocsDirectory);

        first.Select(document => document.Content).ShouldBe(second.Select(document => document.Content));
    }
}

/// <summary>Shared fixtures, so a change to the shape is one edit rather than nine.</summary>
internal static class Sample
{
    public static ProjectProfileDocument Document() => new()
    {
        Purpose = "A desktop reviewer for large diffs.",
        Architecture = "A .NET host, a React renderer, and a JSON-RPC bridge between them.",
        Modules =
        [
            new ProjectModule
            {
                Name = "DiffHacker.Core",
                Path = "src/DiffHacker.Core",
                Summary = "Domain types and orchestration.",
                RelatedModules = ["DiffHacker.Git"],
            },
            new ProjectModule
            {
                Name = "DiffHacker.Git",
                Path = "src/DiffHacker.Git",
                Summary = "The git command line, behind an allowlist.",
            },
        ],
        Layering = "Core references nothing above it.",
        Patterns = "- Errors cross the bridge as codes, never as prose.",
        EntryPoints = [new ProjectEntryPoint { Path = "src/DiffHacker.Host/Program.cs", Purpose = "Composition root." }],
        TestLayout = "xUnit under tests/, one project per source project.",
        DocumentationSources = ["README.md", "CLAUDE.md"],
    };

    public static ProjectProfile Profile() => new()
    {
        RepositoryPath = "/repo",
        Document = Document(),
        Provenance = new ProfileProvenance
        {
            CommitSha = "abc12345def",
            GeneratedAtUtc = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            ProviderDisplayName = "Test provider",
            Model = "test-model",
            DocumentCharacters = 500,
        },
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
    };
}
