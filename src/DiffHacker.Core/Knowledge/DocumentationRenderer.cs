using System.Globalization;
using System.Text;

namespace DiffHacker.Core.Knowledge;

/// <summary>Where generated documentation would be written, if the user chooses to export it.</summary>
public enum DocumentationTarget
{
    /// <summary>The repository root, beside the README.</summary>
    RepositoryRoot,

    /// <summary>A <c>docs/</c> directory, created if it is not there.</summary>
    DocsDirectory,
}

/// <summary>One file the generator would write.</summary>
public sealed record GeneratedDocument
{
    /// <summary>Repository-relative, forward-slashed.</summary>
    public required string RelativePath { get; init; }

    public required string Content { get; init; }
}

/// <summary>
/// Renders a stored profile into three documentation files.
/// <para>
/// Deterministic, with no second model run. The profile is already the model's considered answer
/// about the repository; asking a model to expand it into prose would cost another run, take
/// minutes, and produce documents that can disagree with the profile every analysis actually uses.
/// A template cannot say anything the profile does not.
/// </para>
/// <para>
/// The files live inside DiffHacker. Writing them into the user's repository is a separate,
/// explicitly confirmed export — see <c>DocumentationPreview</c> and the write path in the host.
/// </para>
/// </summary>
public static class DocumentationRenderer
{
    public const string ArchitectureFile = "ARCHITECTURE.md";
    public const string ModulesFile = "MODULES.md";
    public const string ConventionsFile = "CONVENTIONS.md";

    /// <summary>
    /// The three documents, in the order they would be written. Throws when there is no generated
    /// document to render: documentation derived from nothing would be documentation that invents.
    /// </summary>
    public static IReadOnlyList<GeneratedDocument> Render(ProjectProfile profile, DocumentationTarget target)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Document is not { } document)
        {
            throw new InvalidOperationException(
                "There is no generated profile for this repository, so there is nothing to render.");
        }

        var prefix = target is DocumentationTarget.DocsDirectory ? "docs/" : string.Empty;
        var footer = Footer(profile);

        return
        [
            new GeneratedDocument
            {
                RelativePath = prefix + ArchitectureFile,
                Content = Architecture(document, footer),
            },
            new GeneratedDocument
            {
                RelativePath = prefix + ModulesFile,
                Content = Modules(document, footer),
            },
            new GeneratedDocument
            {
                RelativePath = prefix + ConventionsFile,
                Content = Conventions(document, footer),
            },
        ];
    }

    private static string Architecture(ProjectProfileDocument document, string footer)
    {
        var builder = new StringBuilder("# Architecture\n\n");

        Section(builder, null, document.Purpose);
        Section(builder, "How it fits together", document.Architecture);

        if (document.EntryPoints.Count > 0)
        {
            builder.Append("## Entry points\n\n");

            foreach (var entryPoint in document.EntryPoints)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"- `{entryPoint.Path}` — {entryPoint.Purpose.Trim()}\n");
            }

            builder.Append('\n');
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"The modules named here are described in [{ModulesFile}]({ModulesFile}); the rules they "
            + $"follow are in [{ConventionsFile}]({ConventionsFile}).\n");

        if (document.DocumentationSources.Count > 0)
        {
            builder.Append("\nBuilt in part from ");
            builder.Append(string.Join(", ", document.DocumentationSources.Select(source => $"`{source}`")));
            builder.Append(".\n");
        }

        return builder.Append(footer).ToString();
    }

    private static string Modules(ProjectProfileDocument document, string footer)
    {
        var builder = new StringBuilder("# Module map\n\n");

        if (document.Modules.Count == 0)
        {
            builder.Append("No modules were identified for this repository.\n\n");
        }
        else
        {
            builder.Append("| Module | Path | Responsibility |\n|---|---|---|\n");

            foreach (var module in document.Modules)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"| [{module.Name}](#{Anchor(module.Name)}) | `{module.Path}` | {OneLine(module.Summary)} |\n");
            }

            builder.Append('\n');

            foreach (var module in document.Modules)
            {
                builder.Append(CultureInfo.InvariantCulture, $"## {module.Name}\n\n");
                builder.Append(CultureInfo.InvariantCulture, $"`{module.Path}`\n\n");
                builder.Append(module.Summary.Trim()).Append("\n\n");

                if (module.RelatedModules.Count > 0)
                {
                    builder.Append("Closely related to ");
                    builder.Append(string.Join(", ", module.RelatedModules.Select(
                        related => $"[{related}](#{Anchor(related)})")));
                    builder.Append(".\n\n");
                }
            }
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"The shape these modules make up is described in [{ArchitectureFile}]({ArchitectureFile}).\n");

        return builder.Append(footer).ToString();
    }

    private static string Conventions(ProjectProfileDocument document, string footer)
    {
        var builder = new StringBuilder("# Conventions\n\n");

        Section(builder, "Layering", document.Layering);
        Section(builder, "Patterns", document.Patterns);
        Section(builder, "Tests", document.TestLayout);

        builder.Append(CultureInfo.InvariantCulture,
            $"The modules these rules apply to are listed in [{ModulesFile}]({ModulesFile}).\n");

        return builder.Append(footer).ToString();
    }

    /// <summary>
    /// Says where the file came from, so that a reader who finds it in a repository knows it was
    /// generated and from which commit — and so that a future run can be compared against it.
    /// </summary>
    private static string Footer(ProjectProfile profile)
    {
        var builder = new StringBuilder("\n---\n\nGenerated by DiffHacker");

        if (profile.Provenance is { } provenance)
        {
            builder.Append(" on ").Append(
                provenance.GeneratedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (provenance.CommitSha is { Length: > 0 } commit)
            {
                builder.Append(" from commit ").Append(commit.Length > 8 ? commit[..8] : commit);
            }
        }

        return builder.Append(". Edit the profile in DiffHacker and export again rather than editing "
            + "this file, which the next export replaces.\n").ToString();
    }

    private static void Section(StringBuilder builder, string? heading, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (heading is not null)
        {
            builder.Append("## ").Append(heading).Append("\n\n");
        }

        builder.Append(body.Trim()).Append("\n\n");
    }

    /// <summary>Collapses a summary onto one line so it can sit in a table cell.</summary>
    private static string OneLine(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>GitHub's heading-anchor rule, enough of it for module names.</summary>
    private static string Anchor(string heading)
    {
        var builder = new StringBuilder(heading.Length);

        foreach (var c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
            else if (c is ' ')
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }
}
