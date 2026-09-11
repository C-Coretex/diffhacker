using DiffHacker.Core.Settings;

namespace DiffHacker.Core.Analyses;

/// <summary>
/// What a run asks for when the reviewer did not say otherwise: the defaults set in Settings, and
/// how one run's overrides are laid over them.
/// <para>
/// The defaults are written only by <see cref="SaveAsync"/>, which is to say only from Settings. A
/// run never writes them: an override is for the run it was made for, and the next run starts from
/// the defaults again. Before 1.15 the last run's choice silently became the default, which meant a
/// reviewer who trimmed one expensive re-run had trimmed every run after it without being told.
/// </para>
/// <para>
/// Application-wide rather than per repository, like the grouping the reviewer last chose: it is a
/// habit about how someone reads and what they are willing to pay for, not a fact about a project.
/// The two keys that existed before are kept, so a reviewer who had turned the second grouping off
/// keeps it off.
/// </para>
/// </summary>
public sealed class AnalysisDefaults(IAppSettingStore settings)
{
    private const string ChangeClustersKey = "analysis.grouping.clusters";

    private const string ImplementationGroupsKey = "analysis.implementationGroups.produce";

    private const string RisksKey = "analysis.risks.produce";

    private const string NodeExplanationsKey = "analysis.explanations.nodes";

    private const string EdgeExplanationsKey = "analysis.explanations.edges";

    private const string ContainerExplanationsKey = "analysis.explanations.containers";

    private const string VerbosityKey = "analysis.verbosity";

    /// <summary>
    /// The stored defaults. A part never set is on, and a verbosity never set — or set to something
    /// this build cannot read — is <see cref="AnalysisVerbosity.Brief"/>.
    /// </summary>
    public async Task<AnalysisRunOptions> GetAsync(CancellationToken cancellationToken) => new()
    {
        ChangeClusters = await FlagAsync(ChangeClustersKey, cancellationToken).ConfigureAwait(false),
        ImplementationGroups = await FlagAsync(ImplementationGroupsKey, cancellationToken).ConfigureAwait(false),
        Risks = await FlagAsync(RisksKey, cancellationToken).ConfigureAwait(false),
        NodeExplanations = await FlagAsync(NodeExplanationsKey, cancellationToken).ConfigureAwait(false),
        EdgeExplanations = await FlagAsync(EdgeExplanationsKey, cancellationToken).ConfigureAwait(false),
        ContainerExplanations = await FlagAsync(ContainerExplanationsKey, cancellationToken).ConfigureAwait(false),
        Verbosity = AnalysisVerbosityNames.Parse(
                await settings.GetAsync(VerbosityKey, cancellationToken).ConfigureAwait(false))
            ?? AnalysisRunOptions.Default.Verbosity,
    };

    /// <summary>Replaces the stored defaults, and returns them as they now read back.</summary>
    public async Task<AnalysisRunOptions> SaveAsync(AnalysisRunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        await SetFlagAsync(ChangeClustersKey, options.ChangeClusters, cancellationToken).ConfigureAwait(false);
        await SetFlagAsync(ImplementationGroupsKey, options.ImplementationGroups, cancellationToken).ConfigureAwait(false);
        await SetFlagAsync(RisksKey, options.Risks, cancellationToken).ConfigureAwait(false);
        await SetFlagAsync(NodeExplanationsKey, options.NodeExplanations, cancellationToken).ConfigureAwait(false);
        await SetFlagAsync(EdgeExplanationsKey, options.EdgeExplanations, cancellationToken).ConfigureAwait(false);
        await SetFlagAsync(ContainerExplanationsKey, options.ContainerExplanations, cancellationToken).ConfigureAwait(false);

        await settings
            .SetAsync(VerbosityKey, AnalysisVerbosityNames.Of(options.Verbosity), cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What one run asks for: each part the caller named, and the stored default for every part it
    /// did not. Absent means "the default" rather than "no", so a renderer that does not know about a
    /// part cannot silently make analyses cheaper and less useful. Writes nothing.
    /// </summary>
    public async Task<AnalysisRunOptions> ResolveAsync(
        AnalysisRunOverrides overrides,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        var defaults = await GetAsync(cancellationToken).ConfigureAwait(false);

        return new AnalysisRunOptions
        {
            ChangeClusters = overrides.ChangeClusters ?? defaults.ChangeClusters,
            ImplementationGroups = overrides.ImplementationGroups ?? defaults.ImplementationGroups,
            Risks = overrides.Risks ?? defaults.Risks,
            NodeExplanations = overrides.NodeExplanations ?? defaults.NodeExplanations,
            EdgeExplanations = overrides.EdgeExplanations ?? defaults.EdgeExplanations,
            ContainerExplanations = overrides.ContainerExplanations ?? defaults.ContainerExplanations,
            Verbosity = overrides.Verbosity ?? defaults.Verbosity,
        };
    }

    private async Task<bool> FlagAsync(string key, CancellationToken cancellationToken)
    {
        var value = await settings.GetAsync(key, cancellationToken).ConfigureAwait(false);

        return !string.Equals(value, "false", StringComparison.Ordinal);
    }

    private ValueTask SetFlagAsync(string key, bool value, CancellationToken cancellationToken) =>
        settings.SetAsync(key, value ? "true" : "false", cancellationToken);
}

/// <summary>What one run says about its own parts. Null means "the default set in Settings".</summary>
public sealed record AnalysisRunOverrides
{
    public bool? ChangeClusters { get; init; }

    public bool? ImplementationGroups { get; init; }

    public bool? Risks { get; init; }

    public bool? NodeExplanations { get; init; }

    public bool? EdgeExplanations { get; init; }

    public bool? ContainerExplanations { get; init; }

    public AnalysisVerbosity? Verbosity { get; init; }

    /// <summary>Nothing overridden: the run is exactly the defaults.</summary>
    public static AnalysisRunOverrides None { get; } = new();
}
