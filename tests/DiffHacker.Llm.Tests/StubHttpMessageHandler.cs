using System.Net;

namespace DiffHacker.Llm.Tests;

/// <summary>
/// Stands in for every provider. CLAUDE.md is explicit that no test hits a real LLM provider,
/// and a connection test that needed a live key would be untestable by definition.
/// </summary>
internal sealed class StubHttpMessageHandler(
    HttpStatusCode status,
    string body,
    Exception? throwInstead = null)
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;

        if (throwInstead is not null)
        {
            return Task.FromException<HttpResponseMessage>(throwInstead);
        }

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
            RequestMessage = request,
        });
    }
}
