using System.Globalization;
using DiffHacker.Contracts;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace DiffHacker.Host.Rpc;

/// <summary>
/// Demonstrates the bridge in both directions: a typed request that returns a typed result,
/// followed by a stream of server-to-client notifications.
/// <para>
/// Iteration 1 has no product features, so this stands in for one. The
/// <see cref="ProgressNotification"/> shape it emits is the same one the analysis pipeline
/// will use, so this is scaffolding for the protocol rather than throwaway code.
/// </para>
/// </summary>
public sealed class DemoRpcTarget(IRpcNotifier notifier, ILogger<DemoRpcTarget> logger)
{
    private const int DefaultDelayMilliseconds = 150;

    [JsonRpcMethod("demo.startCountdown")]
    public StartDemoResponse StartCountdown(StartDemoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Steps < 1)
        {
            throw RpcErrors.Failure(
                "demo_steps_out_of_range",
                $"demo.startCountdown requires at least one step, got {request.Steps}.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["steps"] = request.Steps.ToString(CultureInfo.InvariantCulture),
                });
        }

        var operationId = Guid.NewGuid().ToString("n");
        var delay = TimeSpan.FromMilliseconds(request.DelayMilliseconds ?? DefaultDelayMilliseconds);

        logger.LogInformation(
            "demo.startCountdown {OperationId}: {Steps} step(s)", operationId, request.Steps);

        // Return immediately; the notifications stream behind the response.
        _ = Task.Run(() => StreamProgressAsync(operationId, request.Steps, delay));

        return new StartDemoResponse(operationId, request.Steps);
    }

    private async Task StreamProgressAsync(string operationId, int steps, TimeSpan delay)
    {
        try
        {
            for (var step = 0; step < steps; step++)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }

                await notifier.NotifyAsync(
                    IRpcNotifier.ProgressMethod,
                    new ProgressNotification(
                        completed: step == steps - 1,
                        // A resource key, not a sentence: the renderer resolves it.
                        message: "demo.step",
                        operationId: operationId,
                        step: step,
                        totalSteps: steps)).ConfigureAwait(false);
            }

            logger.LogInformation("demo.startCountdown {OperationId} completed", operationId);
        }
#pragma warning disable CA1031 // Nothing above this fire-and-forget task can observe the failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "demo.startCountdown {OperationId} failed", operationId);
        }
    }
}
