using Serilog.Events;
using Serilog.Formatting;

namespace DiffHacker.Host.Logging;

/// <summary>
/// The last thing a log event passes through before it reaches the file.
/// <para>
/// Sits at the sink, not at the call site: every message, property and exception in
/// <c>log.txt</c> has been through it, so a future call site cannot leak a credential by
/// forgetting to redact. Properties whose <em>name</em> marks them as credentials are replaced
/// before rendering; the rendered line is then scrubbed for credential-shaped <em>values</em>,
/// which is what covers exception text.
/// </para>
/// </summary>
public sealed class RedactingTextFormatter(ITextFormatter inner) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        using var rendered = new StringWriter();
        inner.Format(Redact(logEvent), rendered);
        output.Write(SecretRedactor.Scrub(rendered.ToString()));
    }

    private static LogEvent Redact(LogEvent logEvent)
    {
        var sensitive = logEvent.Properties
            .Where(property => SecretRedactor.IsSensitiveName(property.Key))
            .Select(property => property.Key)
            .ToArray();

        if (sensitive.Length == 0)
        {
            return logEvent;
        }

        var copy = new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            logEvent.MessageTemplate,
            logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)));

        foreach (var name in sensitive)
        {
            copy.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SecretRedactor.Placeholder)));
        }

        return copy;
    }
}
