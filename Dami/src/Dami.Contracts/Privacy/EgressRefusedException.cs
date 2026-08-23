namespace Dami.Contracts.Privacy;

/// <summary>Thrown when the egress boundary refuses a request.</summary>
/// <remarks>
/// An exception rather than a null or a status, deliberately. A refused egress is a
/// caller doing something the architecture forbids, and it must be loud — code that
/// silently degrades when the boundary blocks it would hide exactly the drift D-012
/// warns about.
/// </remarks>
public sealed class EgressRefusedException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public EgressRefusedException(string reason)
        : base(reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
    }
}
