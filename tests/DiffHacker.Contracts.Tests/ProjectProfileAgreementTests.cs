using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace DiffHacker.Contracts.Tests;

/// <summary>
/// One module shape, three generated records; one entry-point shape, three more; one target enum,
/// three again.
/// <para>
/// A schema cannot reference a definition in another file without the generator copying the type
/// into every output, so the profile document the model answers with, the state the screen reads
/// and the request it saves each carry their own copy. That is a survivable amount of duplication
/// only while something checks the copies cannot drift — which is this.
/// </para>
/// <para>
/// Compared by property name and type rather than by name alone: a <c>relatedModules</c> that was
/// a string on one side and a list on the other would pass a name check and fail at the boundary.
/// </para>
/// </summary>
public sealed class ProjectProfileAgreementTests
{
    [Fact]
    public void The_three_module_shapes_agree()
    {
        var expected = Shape<ProjectModuleInfo>();

        Shape<ProfileModuleInfo>().ShouldBe(expected);
        Shape<SaveModuleInfo>().ShouldBe(expected);
    }

    [Fact]
    public void The_module_shape_is_the_one_the_renderer_was_written_against()
    {
        // Pinned so that dropping a field from all three copies at once is still a failure. Every
        // one of these is read by the profile screen or written into MODULES.md.
        Shape<ProjectModuleInfo>().Select(entry => entry.Name).ShouldBe(
            ["name", "path", "relatedModules", "summary"],
            ignoreOrder: true);
    }

    [Fact]
    public void The_three_entry_point_shapes_agree()
    {
        var expected = Shape<ProjectEntryPointInfo>();

        Shape<ProfileEntryPointInfo>().ShouldBe(expected);
        Shape<SaveEntryPointInfo>().ShouldBe(expected);
    }

    [Fact]
    public void The_three_documentation_target_enums_agree()
    {
        string[] expected = ["repository_root", "docs_directory"];

        WireValues<DocumentationRequestTarget>().ShouldBe(expected, ignoreOrder: true);
        WireValues<DocumentationPreviewTarget>().ShouldBe(expected, ignoreOrder: true);
        WireValues<DocumentationExportRequestTarget>().ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void The_generated_document_and_the_save_request_describe_the_same_profile()
    {
        // The request carries the repository path as well, because a save has to say which
        // repository it is for. Everything else must match, or an edit would silently drop a
        // section the model produced.
        var request = Shape<SaveProfileRequest>()
            .Where(entry => entry.Name != "repositoryPath")
            .Select(entry => entry.Name);

        var document = Shape<ProjectProfileDocument>().Select(entry => entry.Name);

        request.ShouldBe(document, ignoreOrder: true);
    }

    /// <summary>
    /// A record's wire shape: its JSON property names paired with the simple name of each type,
    /// so that two generated copies can be compared without their element types — which differ by
    /// namespace-local name — getting in the way.
    /// </summary>
    private static (string Name, string Type)[] Shape<T>() =>
        [.. typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (
                Name: property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
                Type: Describe(property.PropertyType)))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)];

    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.Name}<{string.Join(',', type.GetGenericArguments().Select(argument => argument.Name))}>"
            : type.Name;

    private static string[] WireValues<TEnum>()
        where TEnum : struct, Enum =>
        [.. typeof(TEnum)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                .Cast<EnumMemberAttribute>()
                .FirstOrDefault()?.Value ?? field.Name)];
}
