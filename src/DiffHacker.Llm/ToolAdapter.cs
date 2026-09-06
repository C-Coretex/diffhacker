using System.Text.Json;
using DiffHacker.Core.Llm;
using Microsoft.Extensions.AI;

namespace DiffHacker.Llm;

/// <summary>
/// Presents a <see cref="LlmToolDefinition"/> to Microsoft.Extensions.AI.
/// <para>
/// This is the seam that keeps <c>DiffHacker.Core</c> free of MEAI (§0.3). Core describes a
/// tool as a name, a description, a JSON Schema and a delegate over raw JSON; MEAI wants an
/// <see cref="AIFunction"/>. Nothing else in the application has to know that.
/// </para>
/// <para>
/// Built by hand rather than through <c>AIFunctionFactory</c>, which derives its schema from a
/// delegate's signature. Here the schema is authored — by Iteration 5's toolbox, and read by
/// the model as prompt text — so it has to survive verbatim.
/// </para>
/// </summary>
internal static class ToolAdapter
{
    public static AIFunction ToAiFunction(LlmToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new DefinitionFunction(definition);
    }

    private sealed class DefinitionFunction : AIFunction
    {
        private readonly LlmToolDefinition _definition;

        public DefinitionFunction(LlmToolDefinition definition)
        {
            _definition = definition;
            JsonSchema = JsonDocument.Parse(definition.ParametersSchemaJson).RootElement.Clone();
        }

        public override string Name => _definition.Name;

        public override string Description => _definition.Description;

        public override JsonElement JsonSchema { get; }

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            // Unreachable in normal operation: LlmSession dispatches tools itself so it can
            // budget, time and trace them. Implemented anyway so this type is a working
            // AIFunction rather than a decorative one, and so a caller that does invoke it
            // gets the tool rather than a surprise.
            var json = JsonSerializer.Serialize(
                arguments as IDictionary<string, object?>,
                SerializerOptions);

            var result = await _definition.Invoke(json, cancellationToken).ConfigureAwait(false);
            return result.Content;
        }

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    }
}
