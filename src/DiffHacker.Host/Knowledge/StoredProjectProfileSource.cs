using DiffHacker.Core.Knowledge;
using DiffHacker.Core.Settings;
using DiffHacker.Tools;

namespace DiffHacker.Host.Knowledge;

/// <summary>
/// What <c>get_project_profile</c> returns, once there is something to return.
/// <para>
/// Iteration 5 shipped the tool with <c>NoProjectProfile</c> behind it, so that the tool surface
/// external agents are written against would not have to grow a method later. This is the
/// implementation that seam was cut for.
/// </para>
/// <para>
/// It lives in the host rather than in the toolbox because the toolbox has no storage and the
/// standalone MCP server has no database — <c>diffhacker-mcp</c> keeps answering "no profile
/// stored", which is true for it.
/// </para>
/// </summary>
public sealed class StoredProjectProfileSource(IProjectProfileStore store) : IProjectProfileSource
{
    public async ValueTask<string?> GetAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var profile = await store.GetAsync(repositoryPath, cancellationToken).ConfigureAwait(false);

        // Null when there is a row but nothing worth sending — the renderer returns null for a
        // profile with no document and no user text, and the tool then says "nothing stored yet",
        // which is the honest answer rather than an empty heading.
        return profile is null ? null : ProfileTextRenderer.Render(profile);
    }
}
