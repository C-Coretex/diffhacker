using System.Globalization;
using System.Text;

namespace DiffHacker.Core.Knowledge;

/// <summary>
/// Turns a stored profile into the text a model reads.
/// <para>
/// One renderer, two consumers, deliberately: this is what <c>get_project_profile</c> returns and
/// what Iteration 7 will put in front of the changed-file list. If those were two renderings, a
/// model could be told two different things about the same repository in the same run.
/// </para>
/// <para>
/// Markdown rather than JSON, for the reason §0.2.9 gives about tool results: the same content
/// costs roughly twice the tokens once every key is quoted, and the model is reading this, not
/// parsing it.
/// </para>
/// <para>
/// The user's sections are rendered last and labelled as the user's own words. That ordering is
/// not cosmetic — a model that reads "ignore the generated/ folder" after the module map treats it
/// as a correction to what came before, which is what the user meant by writing it.
/// </para>
/// </summary>
public static class ProfileTextRenderer
{
    /// <summary>
    /// The generated document alone. This is what the size budget is measured against, so it must
    /// not include anything the user wrote.
    /// </summary>
    public static string RenderDocument(ProjectProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        Section(builder, "Purpose", document.Purpose);
        Section(builder, "Architecture", document.Architecture);

        if (document.Modules.Count > 0)
        {
            builder.Append("## Modules\n\n");

            foreach (var module in document.Modules)
            {
                builder.Append(CultureInfo.InvariantCulture, $"### {module.Name} — `{module.Path}`\n\n");
                builder.Append(module.Summary.Trim()).Append("\n\n");

                if (module.RelatedModules.Count > 0)
                {
                    builder
                        .Append("Related: ")
                        .Append(string.Join(", ", module.RelatedModules))
                        .Append("\n\n");
                }
            }
        }

        Section(builder, "Layering", document.Layering);
        Section(builder, "Patterns and conventions", document.Patterns);

        if (document.EntryPoints.Count > 0)
        {
            builder.Append("## Entry points\n\n");

            foreach (var entryPoint in document.EntryPoints)
            {
                builder.Append(CultureInfo.InvariantCulture, $"- `{entryPoint.Path}` — {entryPoint.Purpose.Trim()}\n");
            }

            builder.Append('\n');
        }

        Section(builder, "Tests", document.TestLayout);

        return builder.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// The whole profile as the model sees it, or null when there is nothing worth sending —
    /// no generated document and no user text either.
    /// </summary>
    public static string? Render(ProjectProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hasUserText =
            !string.IsNullOrWhiteSpace(profile.UserNotes) ||
            !string.IsNullOrWhiteSpace(profile.CustomInstructions);

        if (profile.Document is null && !hasUserText)
        {
            return null;
        }

        var builder = new StringBuilder("# Project profile\n\n");

        if (profile.Provenance is { } provenance)
        {
            builder.Append("Generated ").Append(
                provenance.GeneratedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (provenance.CommitSha is { Length: > 0 } commit)
            {
                builder.Append(" from commit ").Append(Short(commit));
            }

            builder.Append(". It describes the repository as it stood then, not the current changeset.\n\n");
        }

        if (profile.Document is { } document)
        {
            builder.Append(RenderDocument(document)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(profile.UserNotes))
        {
            builder
                .Append("## Notes from the reviewer\n\nWritten by the person who owns this repository. "
                    + "Where it disagrees with anything above, it is right.\n\n")
                .Append(profile.UserNotes.Trim())
                .Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(profile.CustomInstructions))
        {
            builder
                .Append("## Standing instructions\n\nThe reviewer's instructions for every analysis of "
                    + "this repository. Follow them.\n\n")
                .Append(profile.CustomInstructions.Trim())
                .Append("\n\n");
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    /// <summary>Combined length of the parts the user authored, reported but not budgeted.</summary>
    public static int MeasureUserSections(ProjectProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.UserNotes.Length + profile.CustomInstructions.Length;
    }

    private static void Section(StringBuilder builder, string heading, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        builder
            .Append("## ").Append(heading).Append("\n\n")
            .Append(body.Trim())
            .Append("\n\n");
    }

    private static string Short(string commitSha) =>
        commitSha.Length > 8 ? commitSha[..8] : commitSha;
}
