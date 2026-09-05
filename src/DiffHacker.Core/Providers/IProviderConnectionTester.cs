namespace DiffHacker.Core.Providers;

/// <summary>
/// Makes one minimal, real request against a provider to prove the credentials work
/// (Iteration 2, requirement 5).
/// <para>
/// This is deliberately not <c>ILlmSession</c>. Budgets, retries, tool calling and the
/// <c>Microsoft.Extensions.AI</c> abstraction are Iteration 4's job; all this needs is the
/// smallest authenticated request that distinguishes "the key works" from "it does not".
/// </para>
/// </summary>
public interface IProviderConnectionTester
{
    /// <summary>
    /// Authenticates against <paramref name="profile"/>'s provider and returns the models the
    /// key can reach. The model listing costs no tokens on every provider DiffHacker supports,
    /// which is why it is preferred over a completion.
    /// </summary>
    ValueTask<ProviderConnectionResult> TestAsync(
        LlmProviderProfile profile,
        string apiKey,
        CancellationToken cancellationToken);
}
