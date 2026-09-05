using DiffHacker.Host.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace DiffHacker.Host.Tests;

/// <summary>
/// "No secret is ever written to log.txt" is a definition-of-done item for every iteration, so
/// it gets adversarial tests rather than a spot check.
/// </summary>
public sealed class LoggingRedactionTests
{
    private static readonly RedactingTextFormatter Formatter = new(
        new MessageTemplateTextFormatter("{Message:lj} {Properties:j}{NewLine}{Exception}", null));

    [Theory]
    [InlineData("sk-abcdefghijklmnopqrstuvwx")]
    [InlineData("sk-ant-api03-AAAAAAAAAAAAAAAAAAAA")]
    [InlineData("xai-AAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gsk_AAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AIzaSyAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Provider_key_shapes_never_reach_the_file(string secret)
    {
        var rendered = Render(Event($"Calling provider with {secret} now"));

        rendered.ShouldNotContain(secret);
        rendered.ShouldContain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void Authorization_headers_are_stripped()
    {
        var rendered = Render(Event("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"));

        rendered.ShouldNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("OpenAiToken")]
    [InlineData("UserPassword")]
    [InlineData("Authorization")]
    public void A_property_named_as_a_credential_is_redacted_whatever_its_value(string propertyName)
    {
        var logEvent = Event(
            "Configured provider",
            new LogEventProperty(propertyName, new ScalarValue("hunter2-not-a-known-shape")));

        var rendered = Render(logEvent);

        rendered.ShouldNotContain("hunter2");
        rendered.ShouldContain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void A_secret_inside_an_exception_is_stripped_too()
    {
        // Nothing at the call site can redact this, which is exactly why redaction lives here.
        var logEvent = Event(
            "Request failed",
            exception: new InvalidOperationException("401 for key sk-abcdefghijklmnopqrstuvwx"));

        Render(logEvent).ShouldNotContain("sk-abcdefghijklmnopqrstuvwx");
    }

    [Fact]
    public void Ordinary_diagnostics_pass_through_untouched()
    {
        var logEvent = Event(
            "Opened repository",
            new LogEventProperty("Path", new ScalarValue("/home/dev/project")),
            new LogEventProperty("FileCount", new ScalarValue(312)));

        var rendered = Render(logEvent);

        rendered.ShouldContain("Opened repository");
        rendered.ShouldContain("/home/dev/project");
        rendered.ShouldContain("312");
        rendered.ShouldNotContain(SecretRedactor.Placeholder);
    }

    [Fact]
    public void A_property_name_that_merely_contains_key_is_not_over_redacted()
    {
        // "Key" alone is too common to treat as a credential — KeyCount, PrimaryKey, KeyPath.
        SecretRedactor.IsSensitiveName("KeyCount").ShouldBeFalse();
        SecretRedactor.IsSensitiveName("ApiKey").ShouldBeTrue();
    }

    [Fact]
    public void The_real_sink_writes_a_structured_line_to_log_txt()
    {
        var directory = Directory.CreateTempSubdirectory("diffhacker-log");
        try
        {
            var paths = new AppPaths(directory.FullName);

            using (var logger = LoggingSetup.Create(paths, verbose: true))
            {
                logger.Information("Configured {Provider} with {ApiKey}", "openai", "sk-abcdefghijklmnopqrstuvwx");
                Log.CloseAndFlush();
            }

            var written = Directory.EnumerateFiles(paths.LogDirectory, "log*.txt").ToArray();
            written.ShouldNotBeEmpty();

            var text = File.ReadAllText(written[0]);
            text.ShouldContain("Configured");
            text.ShouldContain("openai");
            text.ShouldNotContain("sk-abcdefghijklmnopqrstuvwx");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static LogEvent Event(string message, params LogEventProperty[] properties) =>
        Event(message, null, properties);

    private static LogEvent Event(string message, Exception? exception, params LogEventProperty[] properties) =>
        new(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception,
            new MessageTemplate([new TextToken(message)]),
            properties);

    private static string Render(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        Formatter.Format(logEvent, writer);
        return writer.ToString();
    }
}
