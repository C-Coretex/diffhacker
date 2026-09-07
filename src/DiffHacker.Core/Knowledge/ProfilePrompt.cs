using System.Globalization;
using System.Text;

namespace DiffHacker.Core.Knowledge;

/// <summary>
/// What the model is told before it starts exploring.
/// <para>
/// Two things matter here beyond the prose. The first is the documentation rule: reading a
/// repository's own README and architecture notes is the cheapest high-quality context available,
/// and a model left to its own devices will start grepping. So the instruction is explicit and,
/// more usefully, the opening message <i>names the files</i> — a list a model has in front of it is
/// obeyed where a general instruction is drifted from.
/// </para>
/// <para>
/// The second is that this is a file list and nothing more. §0.2.9 forbids bulk-injecting content,
/// and it applies here exactly as it will to the analysis prompt: the model is told which files
/// exist and told to read them, never handed their contents.
/// </para>
/// </summary>
public static class ProfilePrompt
{
    /// <summary>
    /// Filenames worth naming up front, matched case-insensitively against the leaf name, plus the
    /// directories that conventionally hold decision records.
    /// </summary>
    private static readonly string[] DocumentNames =
    [
        "readme", "architecture", "contributing", "claude", "agents", "design", "overview",
    ];

    private static readonly string[] DocumentDirectories =
    [
        "docs/", "doc/", "documentation/",
    ];

    private static readonly string[] DocumentExtensions =
    [
        ".md", ".markdown", ".rst", ".adoc", ".txt",
    ];

    /// <summary>How many documentation paths to name. Beyond this the list stops being read.</summary>
    private const int MaxNamedDocuments = 40;

    public static string SystemPrompt(int characterBudget) =>
        $"""
        You are profiling a source repository so that later code reviews of it start from real
        knowledge instead of guesswork. You are not reviewing a change: there is no diff here, and
        nothing you write should mention one.

        Work in this order, and do not shortcut it:

        1. Call get_project_profile first. If something is already stored, it tells you what is
           already known and what the reviewer has asked you to keep in mind.
        2. Read the repository's own documentation before you look at any code. The opening message
           names the files. README and architecture notes are written by people who know this
           system; ten minutes of their prose is worth a hundred greps. Record which of them you
           read in documentationSources.
        3. Then explore the code to confirm, correct and fill the gaps: get_repository_tree for the
           shape, the manifest files it reveals for the module boundaries, read_file and search_text
           for anything the documentation left vague or got wrong.
        4. Call report_progress whenever the honest answer to "what is it doing?" changes. Someone
           is waiting and watching.

        What makes this profile good:

        - It is about THIS repository. If a sentence would be equally true of any codebase, delete
          it — it is costing tokens on every future review and teaching nothing.
        - Name real directories, real projects, real files. Quote the conventions the repository
          actually holds itself to, including the ones it enforces mechanically.
        - Prefer what a newcomer would get wrong. Obvious things are cheap for a reader to see for
          themselves; the load-bearing oddity is not.
        - Where documentation and code disagree, believe the code and say so.
        - Some files are listed but their contents are withheld — credentials and key material.
          That is deliberate. Note that they exist if they matter; do not try to work around it.

        Size limit: the rendered profile must stay under {characterBudget.ToString("N0", CultureInfo.InvariantCulture)} characters.
        This is a hard limit, not a target, because this text is re-sent on every turn of every
        future review of this repository — length here is paid for many times over. Spend it on
        specifics and cut anything generic.

        Answer with the structured document you were given the shape for, and nothing else.
        """;

    /// <summary>
    /// The opening message: what repository this is, and which of its documents to read first.
    /// </summary>
    public static string OpeningMessage(
        string repositoryName,
        int visibleFileCount,
        IReadOnlyList<string> documentationPaths,
        string customInstructions)
    {
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture,
            $"Repository: {repositoryName}\nFiles git can see: {visibleFileCount:N0}\n\n");

        if (documentationPaths.Count > 0)
        {
            builder.Append("Documentation found in this repository. Read these first, before any "
                + "other tool call that touches code:\n\n");

            foreach (var path in documentationPaths)
            {
                builder.Append("- ").Append(path).Append('\n');
            }

            builder.Append('\n');
        }
        else
        {
            builder.Append("This repository has no documentation files that could be found by name, "
                + "so the code and its manifests are all there is. Start with get_repository_tree.\n\n");
        }

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            builder
                .Append("Standing instructions from the reviewer who owns this repository. These "
                    + "override anything you infer:\n\n")
                .Append(customInstructions.Trim())
                .Append("\n\n");
        }

        return builder
            .Append("Profile it.")
            .ToString();
    }

    /// <summary>
    /// What is said when a document came back over budget. Names both numbers, because "shorten
    /// it" without a target is a request a model satisfies by trimming a sentence.
    /// </summary>
    public static string OverBudgetRepair(int actual, int budget) =>
        string.Create(CultureInfo.InvariantCulture,
            $"""
            That profile renders to {actual:N0} characters and the hard limit is {budget:N0}.

            Send the whole document again, under the limit. Do not drop a section — cut within
            them. The first things to go are sentences that would be true of any repository,
            restatements of what a file name already says, and modules whose summary adds nothing
            to their name. Keep the specifics: paths, real conventions, the things a newcomer gets
            wrong.
            """);

    /// <summary>
    /// Picks the documentation worth naming out of the repository's visible file list.
    /// <para>
    /// Ordered deliberately: a root README before a nested one, and everything shallow before
    /// everything deep, because the list is truncated and the top of a repository explains it
    /// better than the bottom does.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> FindDocumentation(IEnumerable<string> visibleFiles)
    {
        ArgumentNullException.ThrowIfNull(visibleFiles);

        var matches = new List<string>();

        foreach (var path in visibleFiles)
        {
            if (IsDocumentation(path))
            {
                matches.Add(path);
            }
        }

        return
        [
            .. matches
                .OrderBy(path => path.Count(c => c == '/'))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(MaxNamedDocuments),
        ];
    }

    private static bool IsDocumentation(string path)
    {
        var slash = path.LastIndexOf('/');
        var leaf = slash < 0 ? path : path[(slash + 1)..];
        var dot = leaf.LastIndexOf('.');
        var stem = dot < 0 ? leaf : leaf[..dot];
        var extension = dot < 0 ? string.Empty : leaf[dot..];

        var hasDocumentExtension = extension.Length == 0
            ? dot < 0 && DocumentNames.Contains(stem.ToLowerInvariant())
            : DocumentExtensions.Contains(extension.ToLowerInvariant());

        if (!hasDocumentExtension)
        {
            return false;
        }

        foreach (var name in DocumentNames)
        {
            if (stem.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var directory in DocumentDirectories)
        {
            if (path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
