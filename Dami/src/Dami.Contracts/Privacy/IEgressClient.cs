namespace Dami.Contracts.Privacy;

/// <summary>The only way anything in Dami reaches the network beyond localhost.</summary>
/// <remarks>
/// D-012's mechanism. Outbound-capable services take this as a dependency; local-only
/// services receive no egress client at all, and that absence is auditable in the
/// composition root. Every send — allowed or refused — is a durable execution event, so
/// the egress history of the system is a query, not a guess.
/// </remarks>
public interface IEgressClient
{
    /// <summary>Fetches from an allowed destination.</summary>
    /// <exception cref="EgressRefusedException">
    /// The destination is not allowlisted, or the request carries what the policy
    /// refuses to let leave.
    /// </exception>
    Task<EgressResponse> SendAsync(EgressRequest request, CancellationToken cancellationToken);
}
