using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using Anthropic.Exceptions;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Enough of a <see cref="PipelineResponse"/> to build the exception the OpenAI SDK throws.
/// <para>
/// <see cref="ClientResultException"/> reads its status and headers from a response object, so
/// there is no way to construct a realistic one without this. Making it real matters: the
/// <c>Retry-After</c> header is the one instruction the retry policy trusts over its own curve,
/// and a test that could not supply one would not be testing that at all.
/// </para>
/// </summary>
internal static class FakeProviderResponse
{
    /// <summary>
    /// What the OpenAI SDK throws, and therefore what Grok, DeepSeek, Gemini and every
    /// user-supplied compatible endpoint throw.
    /// </summary>
    public static ClientResultException OpenAiStyle(
        int status,
        string body = "",
        string? retryAfter = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (retryAfter is not null)
        {
            headers["Retry-After"] = retryAfter;
        }

        var message = string.IsNullOrEmpty(body) ? $"HTTP {status}" : body;
        return new ClientResultException(message, new Response(status, body, headers));
    }

    /// <summary>
    /// What the Anthropic SDK throws. Its hierarchy is per-status, and it carries the body on
    /// the exception rather than on a response object.
    /// </summary>
    public static AnthropicApiException AnthropicStyle(System.Net.HttpStatusCode status, string body)
    {
        // StatusCode and ResponseBody are `required`, so each branch sets them rather than the
        // switch assigning them afterwards.
        return status switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                new AnthropicUnauthorizedException { StatusCode = status, ResponseBody = body },
            System.Net.HttpStatusCode.Forbidden =>
                new AnthropicForbiddenException { StatusCode = status, ResponseBody = body },
            System.Net.HttpStatusCode.NotFound =>
                new AnthropicNotFoundException { StatusCode = status, ResponseBody = body },
            System.Net.HttpStatusCode.TooManyRequests =>
                new AnthropicRateLimitException { StatusCode = status, ResponseBody = body },
            System.Net.HttpStatusCode.BadRequest =>
                new AnthropicBadRequestException { StatusCode = status, ResponseBody = body },
            _ => new AnthropicUnexpectedStatusCodeException { StatusCode = status, ResponseBody = body },
        };
    }

    private sealed class Response(int status, string body, Dictionary<string, string> headers) : PipelineResponse
    {
        private Stream _contentStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream
        {
            get => _contentStream;
            set => _contentStream = value ?? Stream.Null;
        }

        public override BinaryData Content => BinaryData.FromString(body);

        protected override PipelineResponseHeaders HeadersCore { get; } = new Headers(headers);

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);

        public override void Dispose() => _contentStream.Dispose();
    }

    private sealed class Headers(Dictionary<string, string> values) : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => values.GetEnumerator();

        public override bool TryGetValue(string name, out string? value) => values.TryGetValue(name, out value);

        public override bool TryGetValues(string name, out IEnumerable<string>? values2)
        {
            if (values.TryGetValue(name, out var single))
            {
                values2 = [single];
                return true;
            }

            values2 = null;
            return false;
        }
    }
}
