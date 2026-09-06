using DiffHacker.Core.Changes;

namespace DiffHacker.Core.Tests;

/// <summary>
/// §0.2.3 allows exactly one language-aware behaviour: tagging a file with a language name as
/// metadata. These tests pin that boundary — that the table looks things up and never infers.
/// </summary>
public sealed class LanguageTableTests
{
    [Theory]
    [InlineData("src/DiffHacker.Core/Changes/ChangedFile.cs", "C#")]
    [InlineData("src/ui/src/App.tsx", "TypeScript")]
    [InlineData("main.go", "Go")]
    [InlineData("deep/nested/path/script.py", "Python")]
    [InlineData("Cargo.toml", "TOML")]
    [InlineData("README.md", "Markdown")]
    public void A_known_extension_is_recognised(string path, string expected) =>
        LanguageTable.Detect(path).ShouldBe(expected);

    [Theory]
    [InlineData("build/Dockerfile", "Dockerfile")]
    [InlineData("Makefile", "Makefile")]
    [InlineData("CMakeLists.txt", "CMake")]
    [InlineData(".gitignore", "Git Config")]
    public void A_filename_with_no_extension_is_still_recognised(string path, string expected) =>
        // Checked before the extension, or CMakeLists.txt would be plain text.
        LanguageTable.Detect(path).ShouldBe(expected);

    [Theory]
    [InlineData("data/mystery.qqq")]
    [InlineData("LICENSE.unknown")]
    [InlineData("no-extension-at-all")]
    [InlineData("trailing.")]
    [InlineData("")]
    public void An_unrecognised_name_reports_no_language_rather_than_guessing(string path) =>
        // A wrong label reaches the prompt Iteration 7 sends, and a wrong label is worse than
        // no label.
        LanguageTable.Detect(path).ShouldBeNull();

    [Fact]
    public void Extension_matching_ignores_case()
    {
        LanguageTable.Detect("Legacy/FORM.CS").ShouldBe("C#");
        LanguageTable.Detect("build/DOCKERFILE").ShouldBe("Dockerfile");
    }

    [Fact]
    public void A_dotfile_is_not_treated_as_an_extension()
    {
        // ".gitignore" is a name, not a file with a "gitignore" extension. Getting this wrong
        // would make every dotfile report the language of whatever its name resembles.
        LanguageTable.Detect(".unknownrc").ShouldBeNull();
    }
}
