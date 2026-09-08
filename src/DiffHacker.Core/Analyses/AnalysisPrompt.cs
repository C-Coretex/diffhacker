using System.Globalization;
using System.Text;
using DiffHacker.Core.Changes;
using DiffHacker.Core.Knowledge;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// What the model is told before it starts reviewing.
/// <para>
/// §0.2.9 draws the line this file sits on: the opening message carries the changed-file list, the
/// stored project profile, the reviewer's standing instructions and nothing else. Not one line of
/// a file, not one hunk of a diff. Everything the model wants to read, it reads through a tool —
/// which is also why the list is worth sending in full rather than paged: it is the map, and a
/// model given the whole map spends its budget on the parts that matter instead of on discovering
/// what exists.
/// </para>
/// <para>
/// The system prompt states the rules the validator enforces, in the same words. A model told
/// afterwards that every file needs a node has already spent the run; a model told first usually
/// gets it right the first time, and repair rounds cost real money on a large change.
/// </para>
/// <para>
/// <b>The division of labour with the schema matters, and is easy to lose.</b> Both this prompt and
/// <c>analysis-result.schema.json</c> reach the model on <i>every request</i> — the schema as the
/// response format — so anything said at length in both is paid for twice per turn, up to three
/// hundred times a run. So: the prompt owns the guidance (how to cluster, how to choose a starting
/// point, what rank means), and a schema description owns only what its field is and the constraint
/// the validator will check. Guidance was duplicated into the schema once; it cost ~4,000 characters
/// a turn and taught the model nothing the prompt had not already said.
/// </para>
/// </summary>
public static class AnalysisPrompt
{
    /// <summary>Identifier sent to providers that name their schemas. Underscores only.</summary>
    public const string SchemaName = "analysis_result";

    /// <summary>Key into <c>DiffHacker.Contracts.ContractSchemas</c>.</summary>
    public const string SchemaKey = "analysis-result";

    public static string SystemPrompt() =>
        """
        You are mapping one uncommitted Git change so a reviewer can understand it without opening
        three hundred files alphabetically and rebuilding the change's shape in their head.
        Rebuilding it is your job, not theirs.

        ## How your answer gets read

        The reviewer sees clusters. They open one, start at the node you marked as its starting
        point, and walk down through the nodes after it — each a consequence of, or companion to,
        what they just read. Then the next cluster.

        So the test is not whether your answer is accurate. It is whether someone can read it top
        to bottom, once, and never hit a node and think "what is this, and why now?". Every node
        must make sense given only the nodes before it. That property is what this document is for.

        ## !! THIS IS NOT A DEPENDENCY GRAPH !!

        A program could compute the imports and the call sites. You are here for what a program
        cannot see: why these files changed together, which one explains the rest, what a person
        should read first. The most useful connections often have no code between their two ends —
        a flag and the migration that backfills it, a rename and the docs that mentioned the old
        name, a fix and the test that would have caught it. Those are what a reviewer currently has
        to hold in their own head, and they are exactly what you are being asked for.

        Do not restrict yourself to relationships you could prove with a grep. Give both kinds, and
        say which is which.

        ## How to work

        1. get_project_profile first — what is already known here, cheaper than rediscovering it.
        2. Read the changed-file list in the opening message before calling anything else. It is
           the shape of the whole change; most grouping decisions are visible in it.
        3. get_file_diff on what matters, read_file and search_text on what the diff does not
           explain. Follow the change outwards: who called this, what relied on the old behaviour.
        4. report_progress whenever the honest answer to "what is it doing?" changes. Someone is
           watching.

         ## Tool-result retention

         Older tool results may be pruned from the conversation as the exploration continues. Only
         tool results are pruned; your instructions, opening message and working reasoning remain.
         When a tool reveals an important fact, state it clearly and concisely in your reasoning before moving on
         so it remains available even if the original result is later pruned. If you need a pruned
         result again, call the tool again.

        ## Clusters — one thing the reviewer thinks about at a time

        A container is a set of changes that only make sense together. Of any two files ask: would
        a reviewer want both in their head at the same moment?

        A shared reason is not the same as a code dependency, and this is the part most often got
        wrong. A config flag, the migration that backfills it, the handler that reads it and a
        changelog line may touch nothing in common and still be one cluster, because they are one
        decision.

        - Not a directory. Same folder, unrelated reasons → different containers.
        - Not a project, language or layer. "All the tests" is not a cluster; the tests for one
          behaviour belong with that behaviour.
        - Not a leftovers bin. Unrelated small changes are several small containers.

        One coherent purpose may be a single container. That is a real answer.

        Order containers with displayOrder so earlier ones make later ones comprehensible: the
        decision before its fallout, the capability before the cleanup it allowed.

        ## !! THE STARTING POINT — your most consequential choice !!

        Each container names exactly one entry node: the change that EXPLAINS the others, not its
        echoes. The changed contract, not the fifty call sites. The migration, not the model that
        matches it.

        The test: if a reviewer read only this node, would the rest of the container be
        roughly predictable? If it would still surprise them, you picked a consequence and the
        real starting point is elsewhere.

        It is not the biggest file, not the one with the most changed lines, not the first
        alphabetically.

        ## Rank — the order they are read in

        Rank is a reading path through a container, entry node at 1. Rank by what a reader needs
        first, NOT by importance and NOT by size: a node comes after everything a reader needs in
        order to understand it. Where one decision fans out into twenty call sites, the decision is
        rank 1 and the sites follow it. Where two nodes do not depend on each other, put the one
        that matters more first.

        ## Edges — how reading flows

        An edge means "to understand the target, read from the source". Do not classify it as
        calls, implements or uses — explain it in prose.

        Mark it direct when real code backs it, conceptual when you inferred it from intent,
        workflow or reading order. Conceptual edges are not weaker guesses and not a last resort:
        they are how you say "these belong to the same idea" when no import says it for you. A
        cluster held together by intent should be full of them.

        Aim for every node to be reachable from that container's entry node by following edges.
        That is what makes a cluster a path a reader can walk rather than a pile of files.

        Cross-container edges are fine where reading really does jump between clusters — but never
        instead of putting related things in one container.

        readingOrder is the single path through the whole change: every node once, in the order you
        would use walking someone through it. Normally containers in displayOrder, each one's nodes
        in rank order; depart from that only for a real reason.

        ## !! Rules that are checked !!

        A failure comes straight back to you, so getting these right first time is cheaper:

        - EVERY changed file has at least one node — deleted, binary, lockfiles, generated output,
          ones you think trivial. A file you cannot read still gets a node saying what its addition
          or removal means. Nothing is dropped, summarised away or folded into a neighbour.
        - No node names a file that is not in the changed-file list.
        - One node per file; split only for two genuinely unrelated changes, the second id being
          the path, a '#', and a short slug.
        - Every node is listed by exactly one container. Not two, not none.
        - Every container names exactly one entry node: one of its members, carrying the
          entry_point state, with rank 1.
        - Ranks within a container, and displayOrder across containers, run 1, 2, 3 … with no gaps
          and no repeats.
        - The reading order lists every node once.

        ## Writing

        - whyItChanged is what reviewers need and what models most often waste. "The method was
          updated" is not a reason. "Because the cache key now includes the tenant, every caller
          had to pass one" is.
        - howItAffectsOthers hands the reader on: what is coming next and why.
        - Risks go in risks, never in an explanation, summary or notes. They are read as a separate
          column, and a warning buried in prose is a warning nobody sees.
        - importance is 1–5 and it is a budget, not a compliment. A mechanical rename is a 1 even
          in an important file. If everything is a 5 you have said nothing.
        - Some files are listed with contents withheld — credentials, key material. Give them a
          node describing the file-level change; do not work around it.

        Answer with the structured document alone.
        """;

    /// <summary>
    /// The opening message: what repository this is, what is already known about it, what the
    /// reviewer asked for, and every file that changed.
    /// </summary>
    public static string OpeningMessage(
        string repositoryName,
        Changeset changeset,
        ProjectProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(changeset);

        var builder = new StringBuilder();
        var statistics = changeset.Statistics;

        builder.Append(CultureInfo.InvariantCulture, $"Repository: {repositoryName}\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"Change: {statistics.TotalFiles:N0} file(s), +{statistics.TotalLinesAdded:N0} "
            + $"-{statistics.TotalLinesRemoved:N0}\n");

        builder.Append(CultureInfo.InvariantCulture,
            $"By status: {statistics.ByStatus.Added:N0} added, {statistics.ByStatus.Modified:N0} modified, "
            + $"{statistics.ByStatus.Deleted:N0} deleted, {statistics.ByStatus.Renamed:N0} renamed, "
            + $"{statistics.ByStatus.Copied:N0} copied\n");

        if (statistics.BinaryFiles > 0 || statistics.SubmoduleFiles > 0 || statistics.UntrackedFiles > 0)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"Of those: {statistics.BinaryFiles:N0} binary, {statistics.SubmoduleFiles:N0} submodule, "
                + $"{statistics.UntrackedFiles:N0} untracked\n");
        }

        if (statistics.Languages.Count > 0)
        {
            builder.Append("Languages: ").Append(string.Join(", ", statistics.Languages)).Append('\n');
        }

        if (statistics.Projects.Count > 0)
        {
            builder.Append("Projects: ").Append(string.Join(", ", statistics.Projects)).Append('\n');
        }

        if (!changeset.HunkCountsAvailable)
        {
            builder.Append("Hunk counts could not be attributed for this change, so the hunks column "
                + "reads '-h' throughout.\n");
        }

        builder.Append('\n');

        if (profile is not null && ProfileTextRenderer.Render(profile) is { Length: > 0 } rendered)
        {
            // The same renderer get_project_profile returns, deliberately: a model told two
            // different things about one repository in one conversation has to pick, and it should
            // never have been asked to.
            builder
                .Append("What is already known about this repository:\n\n")
                .Append(rendered)
                .Append("\n\n");
        }
        else
        {
            builder.Append("No project profile has been generated for this repository, so nothing is "
                + "known about it in advance. Explore more carefully than you otherwise would, and "
                + "say plainly where you are inferring.\n\n");
        }

        builder
            .Append("Changed files. ")
            .Append(ChangedFileText.Legend)
            .Append("\n\n");

        foreach (var file in changeset.Files)
        {
            builder.Append(ChangedFileText.Row(file)).Append('\n');
        }

        return builder
            .Append("\nMap this change.")
            .ToString();
    }

    /// <summary>
    /// What is said when the working tree matches HEAD. Not a prompt — the run never starts — but
    /// it lives here because it is the other half of "what would we say about this changeset".
    /// </summary>
    public static string NothingToAnalyse(string repositoryName) =>
        $"There is nothing uncommitted in {repositoryName} to analyse.";
}
