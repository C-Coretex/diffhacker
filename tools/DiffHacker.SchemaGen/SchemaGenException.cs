namespace DiffHacker.SchemaGen;

/// <summary>
/// Raised for conditions the developer can fix by changing something under <c>/schema</c>.
/// Reported as a single-line error rather than a stack trace.
/// </summary>
internal sealed class SchemaGenException(string message) : Exception(message);
