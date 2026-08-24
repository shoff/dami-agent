namespace Dami.Contracts.Privacy;

/// <summary>Begins an explicit async-flow scope for body-capable egress.</summary>
public interface IEgressOperationScopeFactory
{
    /// <summary>Installs one context until the returned scope is disposed.</summary>
    IDisposable Begin(EgressOperationContext context);
}
