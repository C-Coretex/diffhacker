using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// Project attribution decides how the graph is coloured (§0.6: node fill encodes
/// project/module), so getting "nearest wins" wrong turns a monorepo into one undifferentiated
/// blob.
/// <para>
/// The directory listing is injected, so these test the resolution rules rather than the
/// filesystem. Real directories are covered by the Git-layer tests.
/// </para>
/// </summary>
public sealed class ProjectLocatorTests
{
    private const string Root = "/repo";

    [Fact]
    public void The_nearest_manifest_wins_over_one_at_the_repository_root()
    {
        var locator = Build(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [""] = ["package.json", "README.md"],
            ["src"] = [],
            ["src/Web"] = ["package.json"],
            ["src/Web/components"] = ["Button.tsx"],
        });

        var project = locator.Locate("src/Web/components/Button.tsx");

        // Verification item 8, exactly. Attributing this to the root would put every file in a
        // monorepo into one project and make the colouring meaningless.
        project.Name.ShouldBe("Web");
        project.Directory.ShouldBe("src/Web");
        project.Manifest.ShouldBe("src/Web/package.json");
        project.FromManifest.ShouldBeTrue();
    }

    [Fact]
    public void A_dotnet_project_file_names_the_project_after_itself()
    {
        var locator = Build(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [""] = [],
            ["src"] = [],
            ["src/api"] = ["DiffHacker.Api.csproj"],
        });

        var project = locator.Locate("src/api/Controller.cs");

        // The stem of a .csproj is the project's real name, which is often not its folder name.
        project.Name.ShouldBe("DiffHacker.Api");
        project.Manifest.ShouldBe("src/api/DiffHacker.Api.csproj");
    }

    [Fact]
    public void With_no_manifest_anywhere_a_file_falls_back_to_its_top_level_directory()
    {
        var locator = Build(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [""] = ["README.md"],
            ["docs"] = [],
            ["docs/iterations"] = ["iteration-03.md"],
        });

        var project = locator.Locate("docs/iterations/iteration-03.md");

        project.Name.ShouldBe("docs", "Top-level directory is what people mean by 'which part of the repository'.");
        project.Manifest.ShouldBeNull();
        project.FromManifest.ShouldBeFalse();
    }

    [Fact]
    public void A_file_at_the_root_with_no_manifest_falls_back_to_the_repository_name()
    {
        var locator = Build(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [""] = ["README.md"],
        });

        locator.Locate("README.md").Name.ShouldBe("repo");
    }

    [Fact]
    public void A_manifest_at_the_root_names_the_project_after_the_repository()
    {
        var locator = Build(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [""] = ["go.mod"],
            ["internal"] = ["server.go"],
        });

        var project = locator.Locate("internal/server.go");

        project.Name.ShouldBe("repo");
        project.Manifest.ShouldBe("go.mod");
    }

    [Fact]
    public void Each_directory_is_listed_once_however_many_files_it_holds()
    {
        var listings = 0;

        var locator = new ProjectLocator(
            Root,
            directory =>
            {
                listings++;
                return Layout(directory, new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [""] = [],
                    ["src"] = ["package.json"],
                });
            });

        for (var index = 0; index < 200; index++)
        {
            locator.Locate($"src/file{index}.ts");
        }

        // A thousand-file changeset must not become a thousand directory scans. Caching is the
        // difference between a changeset that loads and one that grinds.
        listings.ShouldBe(1, "The one distinct directory should be listed once, not once per file.");
    }

    private static ProjectLocator Build(Dictionary<string, string[]> layout) =>
        new(Root, directory => Layout(directory, layout));

    /// <summary>Maps an absolute directory back to the repository-relative key the test declared.</summary>
    private static List<string> Layout(string absoluteDirectory, Dictionary<string, string[]> layout)
    {
        var relative = Path.GetRelativePath(Root, absoluteDirectory).Replace('\\', '/');
        var key = relative is "." ? string.Empty : relative;

        return layout.TryGetValue(key, out var names) ? [.. names] : [];
    }
}
