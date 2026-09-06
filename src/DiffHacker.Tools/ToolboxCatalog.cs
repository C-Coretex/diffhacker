using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DiffHacker.Core.Llm;
using DiffHacker.Tools.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DiffHacker.Tools;

/// <summary>
/// The toolbox, projected two ways from one set of definitions.
/// <para>
/// This is where the iteration's "never maintain two definitions of one tool" rule is kept
/// structurally rather than by discipline. Every tool is one <c>[McpServerTool]</c> method. This
/// class scans those methods once and produces both <see cref="McpTools"/>, which the stdio
/// server serves, and <see cref="LlmTools"/>, which the in-process analysis runs. Neither list
/// can gain, lose or rename a tool without the other doing the same, because both are built from
/// the same <see cref="MethodInfo"/>.
/// </para>
/// </summary>
public sealed class ToolboxCatalog
{
    /// <summary>
    /// Reserved by <c>DiffHacker.Llm</c>'s structured-output path: in tool-call mode the session
    /// adds a tool of this name and intercepts it. A toolbox tool sharing the name would have its
    /// results eaten as the final answer.
    /// </summary>
    private const string ReservedToolName = "submit_result";

    private ToolboxCatalog(IReadOnlyList<McpServerTool> mcpTools, IReadOnlyList<LlmToolDefinition> llmTools)
    {
        McpTools = mcpTools;
        LlmTools = llmTools;
    }

    /// <summary>The tools as the MCP server serves them.</summary>
    public IReadOnlyList<McpServerTool> McpTools { get; }

    /// <summary>The same tools, as the LLM layer's provider-agnostic contract.</summary>
    public IReadOnlyList<LlmToolDefinition> LlmTools { get; }

    /// <summary>Tool names, for tests and diagnostics.</summary>
    public IReadOnlyList<string> Names => [.. McpTools.Select(tool => tool.ProtocolTool.Name)];

    public static ToolboxCatalog Create(RepositorySession session, ToolboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        var logger = options.LoggerFactory.CreateLogger("DiffHacker.Tools");

        // The tool types, constructed explicitly. Adding one here is the whole of registering its
        // tools: both projections and the stdio server come off this list, and neither can be
        // given a tool the other does not have.
        object[] targets =
        [
            new ChangesetTools(session, options.Git, options.Limits),
            new FileTools(session, options.Git, options.Limits),
            new SearchTools(session, options.Git, options.Limits),
            new ContextTools(
                session,
                options.Profiles,
                options.Progress,
                options.LoggerFactory.CreateLogger<ContextTools>()),
        ];

        var mcp = new List<McpServerTool>();
        var llm = new List<LlmToolDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            var type = target.GetType();

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } attribute)
                {
                    continue;
                }

                var name = attribute.Name
                    ?? throw new InvalidOperationException(
                        $"{type.Name}.{method.Name} must name its tool explicitly. A derived name would "
                        + "change silently if the method were ever renamed, and the name is part of the "
                        + "contract external agents are written against.");

                if (name.Equals(ReservedToolName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"'{ReservedToolName}' is reserved by the structured-output path in DiffHacker.Llm. "
                        + $"Rename {type.Name}.{method.Name}.");
                }

                if (!seen.Add(name))
                {
                    throw new InvalidOperationException(
                        $"Two tools are named '{name}'. Tool names must be unique across the toolbox.");
                }

                // Two projections, one MethodInfo. Each goes through the SDK entry point built
                // for it: McpServerTool.Create knows a method returning string is text and emits
                // it as text, where routing through an AIFunction first would JSON-encode it and
                // hand the model a quoted, backslash-escaped wall.
                //
                // What keeps them one tool rather than two is the shared method plus
                // ToolboxCatalogTests, which asserts the pair agree on name, description and
                // argument schema for every tool.
                var tool = McpServerTool.Create(method, target, new McpServerToolCreateOptions { Name = name });
                var function = AIFunctionFactory.Create(method, target, new AIFunctionFactoryOptions { Name = name });

                mcp.Add(new LoggingMcpServerTool(tool, logger));
                llm.Add(Project(tool.ProtocolTool, function, logger));
            }
        }

        return new ToolboxCatalog(mcp, llm);
    }

    /// <summary>
    /// Turns one MCP tool into the LLM layer's currency.
    /// <para>
    /// The schema is handed over verbatim as text, which is what <c>LlmToolDefinition</c> is
    /// built for and what <c>ToolAdapter</c> preserves on the way to the provider — so the model
    /// sees exactly the schema the MCP client would have seen.
    /// </para>
    /// </summary>
    private static LlmToolDefinition Project(Tool protocolTool, AIFunction function, ILogger logger)
    {
        return new LlmToolDefinition
        {
            Name = protocolTool.Name,
            Description = protocolTool.Description ?? string.Empty,
            ParametersSchemaJson = protocolTool.InputSchema.GetRawText(),
            Invoke = async (argumentsJson, cancellationToken) =>
            {
                var started = Stopwatch.GetTimestamp();

                try
                {
                    var arguments = Parse(argumentsJson);
                    var raw = await function.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
                    var content = Stringify(raw);

                    ToolboxLog.Called(
                        logger,
                        protocolTool.Name,
                        argumentsJson,
                        Encoding.UTF8.GetByteCount(content),
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        truncated: content.Contains("… truncated", StringComparison.Ordinal),
                        failed: false);

                    return LlmToolResult.Success(content);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A tool failure is a result the model corrects, never an exception that ends
                    // the run — the same contract LlmSession already relies on.
                    ToolboxLog.ToolThrew(logger, protocolTool.Name, ex);

                    return LlmToolResult.Failure(
                        $"The tool failed: {ex.Message}. Check the arguments against the schema and try again.");
                }
            },
        };
    }

    /// <summary>
    /// Reads the model's arguments. Whatever it produced may not match the schema, so anything
    /// unparseable becomes an empty argument set and the binder reports the real problem.
    /// </summary>
    private static AIFunctionArguments Parse(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new AIFunctionArguments();
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);

        return parsed is null
            ? new AIFunctionArguments()
            : new AIFunctionArguments(parsed.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
    }

    /// <summary>
    /// Every tool returns a string, but the function layer round-trips it through JSON, so a
    /// plain string arrives as a JSON string element rather than as text.
    /// </summary>
    private static string Stringify(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => result.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Wraps a tool so the stdio path is logged exactly as the in-process path is.
    /// </summary>
    private sealed class LoggingMcpServerTool(McpServerTool inner, ILogger logger) : DelegatingMcpServerTool(inner)
    {
        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await base.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            var text = TextOf(result);

            ToolboxLog.Called(
                logger,
                ProtocolTool.Name,
                request.Params?.Arguments is { } arguments ? JsonSerializer.Serialize(arguments) : string.Empty,
                Encoding.UTF8.GetByteCount(text),
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                truncated: text.Contains("… truncated", StringComparison.Ordinal),
                failed: result.IsError ?? false);

            return result;
        }

        private static string TextOf(CallToolResult result)
        {
            var builder = new StringBuilder();

            foreach (var block in result.Content)
            {
                if (block is TextContentBlock text)
                {
                    builder.Append(text.Text);
                }
            }

            return builder.ToString();
        }
    }
}
