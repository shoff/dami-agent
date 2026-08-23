namespace Dami.Contracts.Privacy;

/// <summary>The rate limit on everything that leaves this machine (C5).</summary>
/// <remarks>
/// The allowlist answers "may this destination be reached at all"; the budget answers
/// "how often may anything be reached". A runaway proactive loop calling the frontier
/// nightly passes every allowlist check — this is the boundary that trips instead.
/// </remarks>
public interface IEgressBudget
{
    /// <summary>Null when within budget; otherwise the reason the attempt must be refused.</summary>
    Task<string?> FindRefusalAsync(CancellationToken cancellationToken);
}
