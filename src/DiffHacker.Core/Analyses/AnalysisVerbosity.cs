using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using DiffHacker.Contracts;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// How much the model is asked to write in each prose field. It changes the lengths the prompt asks
/// for (<see cref="AnalysisFieldBudgets.For"/>) and the point at which a field is reported as too
/// long — never the schema, and never what the validator rejects.
/// </summary>
[JsonConverter(typeof(SchemaEnumConverter<AnalysisVerbosity>))]
public enum AnalysisVerbosity
{
    /// <summary>
    /// About half of <see cref="Medium"/>. The default: most prose is read in a hover card, and a
    /// sentence that fits there is worth more than a paragraph that is clipped.
    /// </summary>
    [EnumMember(Value = "brief")]
    Brief,

    /// <summary>The lengths every analysis before 1.15 was written against.</summary>
    [EnumMember(Value = "medium")]
    Medium,

    /// <summary>About twice <see cref="Medium"/>, for a reviewer who reads the cards rather than skims them.</summary>
    [EnumMember(Value = "detailed")]
    Detailed,
}

/// <summary>The wire spellings, in one place, for the settings store and the JSON-RPC surface.</summary>
public static class AnalysisVerbosityNames
{
    public const string Brief = "brief";

    public const string Medium = "medium";

    public const string Detailed = "detailed";

    public static string Of(AnalysisVerbosity verbosity) => verbosity switch
    {
        AnalysisVerbosity.Brief => Brief,
        AnalysisVerbosity.Medium => Medium,
        AnalysisVerbosity.Detailed => Detailed,
        _ => throw new ArgumentOutOfRangeException(nameof(verbosity), verbosity, "Unnamed verbosity."),
    };

    /// <summary>
    /// Reads a stored spelling back, or null when it is absent or unrecognisable — a value an older
    /// or newer build wrote falls back to the default rather than failing a run.
    /// </summary>
    public static AnalysisVerbosity? Parse(string? value) => value switch
    {
        Brief => AnalysisVerbosity.Brief,
        Medium => AnalysisVerbosity.Medium,
        Detailed => AnalysisVerbosity.Detailed,
        _ => null,
    };
}
