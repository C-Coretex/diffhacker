using DiffHacker.Core.Changes;
using DiffHacker.Core.Tools;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Tools;

/// <summary>
/// Opens the toolbox on a repository.
/// <para>
/// One entry point, used by both composition roots, so the application and the standalone MCP
/// server cannot wire the toolbox differently. They differ in exactly two things, and both are
/// arguments here: which repository, and where <c>report_progress</c> goes.
/// </para>
/// </summary>
public static class Toolbox
{
    /// <summary>
    /// Takes the repository snapshot and builds the catalogue against it.
    /// </summary>
    /// <exception cref="GitClientException">
    /// <paramref name="repositoryPath"/> is not a readable git working tree.
    /// </exception>
    public static async Task<ToolboxCatalog> OpenAsync(
        ToolboxOptions options,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var session = await RepositorySession
            .CreateAsync(options.Git, repositoryPath, cancellationToken)
            .ConfigureAwait(false);

        var catalogue = ToolboxCatalog.Create(session, options);

        ToolboxLog.ToolboxReady(
            options.LoggerFactory.CreateLogger("DiffHacker.Tools"),
            session.Root,
            catalogue.Names.Count,
            session.Changeset.Files.Count,
            session.VisibleFiles.Count);

        return catalogue;
    }
}

/// <summary>
/// Everything the toolbox needs that is not the repository itself.
/// <para>
/// A plain record rather than container registrations: there are five dependencies, two
/// composition roots, and no lifetime question to answer. Explicit construction also means the
/// tests build a toolbox without standing a container up.
/// </para>
/// </summary>
public sealed record ToolboxOptions
{
    public required IGitClient Git { get; init; }

    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>Where <c>report_progress</c> goes: the WebView in the app, the MCP client over stdio.</summary>
    public required IToolProgressSink Progress { get; init; }

    /// <summary>Iteration 6 replaces the default, which answers "no profile stored".</summary>
    public IProjectProfileSource Profiles { get; init; } = new NoProjectProfile();

    public ToolboxLimits Limits { get; init; } = ToolboxLimits.Default;
}
