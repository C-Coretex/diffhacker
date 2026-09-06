namespace DiffHacker.Core.Changes;

/// <summary>
/// Which project or module a file belongs to, resolved by finding the nearest manifest above it.
/// <para>
/// This is metadata and nothing more (§0.2.3). The manifest is located by filename and
/// <b>never opened</b>: no package name, no dependency list, no target framework. The point is
/// that a reviewer and the LLM can both tell "this change is in the web app" from "this change
/// is in the API", not that the application understands any build system.
/// </para>
/// </summary>
/// <param name="Name">
/// Display name for the project. The manifest's own directory name where a manifest was found,
/// otherwise the top-level directory the file sits under, otherwise the repository name.
/// </param>
/// <param name="Directory">
/// Repository-relative directory the attribution is anchored to, or an empty string for the
/// repository root.
/// </param>
/// <param name="Manifest">
/// Repository-relative path of the manifest file that produced the attribution, or null when it
/// came from the fallback.
/// </param>
public readonly record struct ProjectReference(string Name, string Directory, string? Manifest)
{
    /// <summary>True when a real manifest was found rather than the directory fallback.</summary>
    public bool FromManifest => Manifest is not null;
}
