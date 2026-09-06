using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// A scripted provider.
/// <para>
/// CLAUDE.md and Iteration 4 requirement 9 both say no test hits a real provider, and this is
/// what makes that possible: a queue of prepared turns, each either a response or an exception,
/// handed out in order. Every request is recorded, so a test can assert what the session
/// actually asked for — the tools it offered, the response format it chose, the messages it
/// accumulated — rather than only what came back.
/// </para>
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Turn> _script = new();

    /// <summary>Every request, in order.</summary>
    public List<Request> Requests { get; } = [];

    public Request LastRequest => Requests[^1];

    /// <summary>Turns still unused. A test asserting a run stopped early checks this.</summary>
    public int Remaining => _script.Count;

    /// <summary>
    /// Blocks the next response until released. Used to prove cancellation unwinds a request
    /// that is genuinely in flight rather than one that had already finished.
    /// </summary>
    public TaskCompletionSource? Gate { get; set; }

    public FakeChatClient Responds(ChatResponse response)
    {
        _script.Enqueue(new Turn { Response = response });
        return this;
    }

    /// <summary>Answers with plain text and no tool calls.</summary>
    public FakeChatClient Says(string text, UsageDetails? usage = null) =>
        Responds(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = usage ?? Usage(10, 5),
            FinishReason = ChatFinishReason.Stop,
        });

    /// <summary>Answers by asking for one or more tools, all in a single turn.</summary>
    public FakeChatClient Calls(params (string Name, object Arguments)[] calls)
    {
        var contents = calls
            .Select((call, index) => (AIContent)new FunctionCallContent(
                $"call-{Guid.NewGuid():n}"[..12] + index,
                call.Name,
                ToArguments(call.Arguments)))
            .ToList();

        return Responds(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            Usage = Usage(20, 8),
            FinishReason = ChatFinishReason.ToolCalls,
        });
    }

    public FakeChatClient Throws(Exception exception)
    {
        _script.Enqueue(new Turn { Exception = exception });
        return this;
    }

    /// <summary>Throws the same exception <paramref name="times"/> turns in a row.</summary>
    public FakeChatClient ThrowsRepeatedly(Func<Exception> exception, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Throws(exception());
        }

        return this;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(new Request
        {
            Messages = [.. messages],
            Options = options,
        });

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_script.Count == 0)
        {
            throw new InvalidOperationException(
                $"The script ran out after {Requests.Count} request(s). The session asked for more turns than the test prepared.");
        }

        var turn = _script.Dequeue();
        return turn.Exception is not null ? throw turn.Exception : turn.Response!;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Present so the fake is a complete IChatClient. Nothing in DiffHacker streams: the
        // answer is one large JSON document produced at the end, and §0.2.8 forbids revealing
        // a half-built one.
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Disposed = true;

    public bool Disposed { get; private set; }

    public static UsageDetails Usage(long input, long output) => new()
    {
        InputTokenCount = input,
        OutputTokenCount = output,
        TotalTokenCount = input + output,
    };

    private static Dictionary<string, object?> ToArguments(object arguments) =>
        arguments as Dictionary<string, object?>
        ?? arguments.GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(arguments));

    internal sealed record Request
    {
        public required IReadOnlyList<ChatMessage> Messages { get; init; }

        public ChatOptions? Options { get; init; }

        public IReadOnlyList<string> ToolNames =>
            [.. (Options?.Tools ?? []).Select(tool => tool.Name)];

        public string SystemPrompt =>
            Messages.FirstOrDefault(message => message.Role == ChatRole.System)?.Text ?? string.Empty;

        /// <summary>
        /// Tool results as the model would read them. Not reachable through
        /// <c>ChatMessage.Text</c>, which only sees <c>TextContent</c>.
        /// </summary>
        public IReadOnlyList<string> ToolResults =>
        [
            .. Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Select(content => content.Result?.ToString() ?? string.Empty),
        ];
    }

    private sealed record Turn
    {
        public ChatResponse? Response { get; init; }

        public Exception? Exception { get; init; }
    }
}
