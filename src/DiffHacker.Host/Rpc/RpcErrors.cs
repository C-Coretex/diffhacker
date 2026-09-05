using DiffHacker.Contracts;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Builds JSON-RPC errors that carry a stable code rather than English prose.
/// <para>
/// CLAUDE.md §0.6 forbids hardcoded user-facing strings, and .NET resources cannot reach the
/// WebView. So the host sends <see cref="RpcErrorData"/> — a code plus named arguments — and
/// the renderer resolves it through its own string catalogue. The message on the exception is
/// for <c>log.txt</c> and for developers, never for the user interface.
/// </para>
/// </summary>
public static class RpcErrors
{
    /// <summary>JSON-RPC reserves -32000 to -32099 for implementation-defined server errors.</summary>
    public const int ApplicationErrorCode = -32000;

    public static LocalRpcException Failure(
        string code,
        string developerMessage,
        IReadOnlyDictionary<string, string>? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new LocalRpcException(developerMessage)
        {
            ErrorCode = ApplicationErrorCode,
            ErrorData = new RpcErrorData(args, code),
        };
    }
}
