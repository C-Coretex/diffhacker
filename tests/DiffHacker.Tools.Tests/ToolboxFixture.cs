using DiffHacker.Core.Llm;
using DiffHacker.Core.Tools;
using DiffHacker.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffHacker.Tools.Tests;

/// <summary>
/// Opens the toolbox on a fixture repository, the way a composition root does.
/// <para>
/// Every test drives tools through <see cref="ToolboxCatalog.LlmTools"/> — the same projection
/// the analysis pipeline will use — rather than by calling the tool methods directly. Calling the
/// methods would test the code and skip the contract; going through the catalogue also exercises
/// argument binding against the generated schema, which is where a tool most easily breaks
/// without anyone noticing.
/// </para>
/// </summary>
internal sealed class ToolboxFixture : IAsyncDisposable
{
    private ToolboxFixture(FixtureRepository repository, ToolboxCatalog catalogue, RecordingProgressSink progress)
    {
        Repository = repository;
        Catalogue = catalogue;
        Progress = progress;
    }

    public FixtureRepository Repository { get; }

    public ToolboxCatalog Catalogue { get; }

    public RecordingProgressSink Progress { get; }

    public static Task<ToolboxFixture> OpenAsync(
        FixtureRepository repository,
        CancellationToken cancellationToken,
        ToolboxLimits? limits = null) =>
        OpenAsync(repository, repository.Root, cancellationToken, limits);

    public static async Task<ToolboxFixture> OpenAsync(
        FixtureRepository repository,
        string repositoryPath,
        CancellationToken cancellationToken,
        ToolboxLimits? limits = null)
    {
        var progress = new RecordingProgressSink();

        var catalogue = await Toolbox.OpenAsync(
            new ToolboxOptions
            {
                Git = GitClientFactory.Create(),
                LoggerFactory = NullLoggerFactory.Instance,
                Progress = progress,
                Limits = limits ?? ToolboxLimits.Default,
            },
            repositoryPath,
            cancellationToken);

        return new ToolboxFixture(repository, catalogue, progress);
    }

    /// <summary>Calls a tool by name with an anonymous object as its arguments.</summary>
    public async Task<string> CallAsync(string tool, object? arguments = null)
    {
        var result = await InvokeAsync(tool, arguments);
        return result.Content;
    }

    public async ValueTask<LlmToolResult> InvokeAsync(string tool, object? arguments = null)
    {
        var definition = Catalogue.LlmTools.SingleOrDefault(t => t.Name == tool)
            ?? throw new InvalidOperationException(
                $"There is no tool called '{tool}'. The toolbox has: {string.Join(", ", Catalogue.Names)}");

        var json = arguments is null
            ? "{}"
            : System.Text.Json.JsonSerializer.Serialize(arguments);

        return await definition.Invoke(json, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Repository.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Captures <c>report_progress</c> so tests can assert what the reviewer would have seen.</summary>
internal sealed class RecordingProgressSink : IToolProgressSink
{
    private readonly List<ToolProgressReport> _reports = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<ToolProgressReport> Reports
    {
        get
        {
            lock (_gate)
            {
                return [.. _reports];
            }
        }
    }

    public ValueTask ReportAsync(ToolProgressReport report, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        lock (_gate)
        {
            _reports.Add(report);
        }

        return ValueTask.CompletedTask;
    }
}
