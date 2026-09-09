namespace DiffHacker.Core.Analyses;

/// <summary>Whether a diagnostic stops a run or is merely recorded.</summary>
public enum AnalysisDiagnosticSeverity
{
    /// <summary>
    /// The result is not usable as it stands. It goes back to the model with this message, and if
    /// the repair rounds run out the run fails rather than storing something patched.
    /// </summary>
    Error,

    /// <summary>
    /// Worth knowing, not worth rejecting an answer over. A cycle is the main one: mutual
    /// dependencies are real, and asking the model to break one would be asking it to lie.
    /// </summary>
    Warning,
}

/// <summary>
/// One thing validation observed about a result.
/// <para>
/// Every diagnostic names its subject — the node id, container id, edge or file path it is about —
/// because requirement 3 of Iteration 7 is not "reject bad results" but "say specifically what was
/// wrong". A message the model cannot act on costs a repair round and buys nothing.
/// </para>
/// </summary>
public sealed record AnalysisDiagnostic
{
    public required AnalysisDiagnosticSeverity Severity { get; init; }

    /// <summary>A stable <see cref="AnalysisDiagnosticCodes"/> value.</summary>
    public required string Code { get; init; }

    /// <summary>The developer-facing sentence, naming the offending id or path.</summary>
    public required string Message { get; init; }

    /// <summary>What the diagnostic is about. Empty when it is about the result as a whole.</summary>
    public string Subject { get; init; } = string.Empty;

    public static AnalysisDiagnostic Error(string code, string subject, string message) => new()
    {
        Severity = AnalysisDiagnosticSeverity.Error,
        Code = code,
        Subject = subject,
        Message = message,
    };

    public static AnalysisDiagnostic Warning(string code, string subject, string message) => new()
    {
        Severity = AnalysisDiagnosticSeverity.Warning,
        Code = code,
        Subject = subject,
        Message = message,
    };
}

/// <summary>
/// Stable codes for what validation can observe, so the renderer phrases them in its own resource
/// layer instead of showing the host's developer text.
/// </summary>
public static class AnalysisDiagnosticCodes
{
    // Errors — the rules of Iteration 7 requirement 3.
    public const string FileNotCovered = "file_not_covered";
    public const string UnknownFile = "unknown_file";
    public const string DuplicateNodeId = "duplicate_node_id";
    public const string NodeIdNotDerived = "node_id_not_derived";
    public const string NodeInNoContainer = "node_in_no_container";
    public const string NodeInManyContainers = "node_in_many_containers";
    public const string UnknownNodeReference = "unknown_node_reference";
    public const string DuplicateContainerId = "duplicate_container_id";
    public const string NoEntryNode = "no_entry_node";
    public const string ManyEntryNodes = "many_entry_nodes";
    public const string EntryNodeNotAMember = "entry_node_not_a_member";
    public const string EntryNodeNotFirst = "entry_node_not_first";
    public const string EntryStateDisagrees = "entry_state_disagrees";
    public const string RankNotDense = "rank_not_dense";
    public const string DisplayOrderNotDense = "display_order_not_dense";
    public const string ImportanceOutOfRange = "importance_out_of_range";
    public const string EmptyNodeField = "empty_node_field";
    public const string EmptyContainer = "empty_container";
    public const string DuplicateReadingOrderEntry = "duplicate_reading_order_entry";
    public const string DanglingEdge = "dangling_edge";

    // Warnings — recorded on the stored result, never a reason to reject an answer.
    public const string Cycle = "cycle";
    public const string SelfEdge = "self_edge";
    public const string IncompleteReadingOrder = "incomplete_reading_order";

    /// <summary>
    /// A node its container's entry node cannot reach by following edges.
    /// <para>
    /// The point of the graph is that a reviewer walks it: start here, then read this because of
    /// that. A node nothing leads to is one the reviewer arrives at with no idea why, which is the
    /// state this application exists to remove. A warning rather than an error because the node may
    /// genuinely belong to the cluster with the connection left unsaid — but it is worth saying that
    /// the path has a gap in it.
    /// </para>
    /// </summary>
    public const string UnreachableNode = "unreachable_node";

    /// <summary>
    /// A written field that ran to more than twice its <see cref="AnalysisFieldBudgets"/> length.
    /// <para>
    /// A warning, and deliberately never an error. The alternative — a <c>maxLength</c> in the
    /// result schema — would be enforced by the provider, and a document three hundred nodes long
    /// would be rejected and rewritten because one sentence ran long. Nothing about the answer is
    /// wrong; it just will not fit in the box it is read in, so the renderer truncates it and this
    /// records that it had to.
    /// </para>
    /// </summary>
    public const string VerboseField = "verbose_field";
}
