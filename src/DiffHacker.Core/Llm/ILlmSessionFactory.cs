using DiffHacker.Core.Providers;

namespace DiffHacker.Core.Llm;

/// <summary>
/// Builds a session for a configured provider.
/// <para>
/// The factory reads the API key from <c>ISecretStore</c> itself, so no caller ever holds one.
/// That is the same rule <c>ProviderRpcTarget</c> follows and the reason §0.2.13 has held so
/// far: a key that nothing above this line can name is a key nothing above this line can leak.
/// </para>
/// </summary>
public interface ILlmSessionFactory
{
    /// <summary>
    /// Creates a session for <paramref name="profile"/>.
    /// </summary>
    /// <exception cref="LlmConfigurationException">
    /// The profile cannot be used: no key stored, or an OpenAI-compatible profile with no base
    /// URL. Thrown rather than returned because it is a configuration mistake to fix in
    /// settings, not an outcome of a run.
    /// </exception>
    ValueTask<ILlmSession> CreateAsync(
        LlmProviderProfile profile,
        LlmBudget budget,
        CancellationToken cancellationToken);
}

/// <summary>
/// A provider profile that cannot be used as configured.
/// </summary>
public sealed class LlmConfigurationException(string failureCode, string message)
    : Exception(message)
{
    /// <summary>A <see cref="LlmFailures"/>-style code, so the host can translate it.</summary>
    public string FailureCode { get; } = failureCode;
}
