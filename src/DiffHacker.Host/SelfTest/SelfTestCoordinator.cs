using System.Text.Json;
using DiffHacker.Contracts;
using Microsoft.Extensions.Logging;

namespace DiffHacker.Host.SelfTest;

/// <summary>
/// Collects the renderer's verdict when the host runs with <c>--self-test</c>.
/// <para>
/// This is what CI gates on. A screenshot proves a window appeared; it does not prove the
/// bridge works, and a blank WebView photographs just as well as a working one. So the
/// renderer performs the round trip itself and reports back, and the process exit code
/// carries the answer.
/// </para>
/// </summary>
public sealed class SelfTestCoordinator(ILogger<SelfTestCoordinator> logger)
{
    private readonly TaskCompletionSource<SelfTestResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Report(SelfTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!_completion.TrySetResult(result))
        {
            logger.LogWarning("Ignored a duplicate self-test report");
            return;
        }

        foreach (var check in result.Checks)
        {
            logger.LogInformation(
                "Self-test check {Check}: {Outcome}{Detail}",
                check.Name,
                check.Passed ? "passed" : "FAILED",
                string.IsNullOrEmpty(check.Detail) ? string.Empty : $" — {check.Detail}");
        }
    }

    /// <summary>
    /// Waits for the renderer's report. Returns <see langword="null"/> when it never arrives,
    /// which is itself a failure: it means the renderer did not reach the bridge.
    /// </summary>
    public async Task<SelfTestResult?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogError("Self-test timed out after {Timeout}", timeout);
            return null;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Self-test was cancelled before the renderer reported");
            return null;
        }
    }

    public static async Task WriteResultAsync(string path, SelfTestResult? result, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var payload = result ?? new SelfTestResult(
            [new SelfTestCheck("The renderer did not report before the timeout elapsed.", "renderer_reported", false)],
            false);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(payload, SelfTestJson.Options),
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class SelfTestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
