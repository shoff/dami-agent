using Dami.Contracts.Privacy;

namespace Dami.Privacy;

/// <summary>Flows explicit egress provenance through async HTTP call chains.</summary>
public sealed class AmbientEgressOperationContext :
    IEgressOperationContextReader,
    IEgressOperationScopeFactory
{
    private readonly AsyncLocal<Scope?> current = new();

    /// <inheritdoc />
    public EgressOperationContext? Current => this.current.Value?.Context;

    /// <inheritdoc />
    public IDisposable Begin(EgressOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = new Scope(this, context, this.current.Value);
        this.current.Value = scope;
        return scope;
    }

    private sealed class Scope(
        AmbientEgressOperationContext owner,
        EgressOperationContext context,
        Scope? previous) : IDisposable
    {
        private AmbientEgressOperationContext? owner = owner;

        public EgressOperationContext Context { get; } = context;

        public void Dispose()
        {
            AmbientEgressOperationContext? activeOwner = Volatile.Read(ref this.owner);
            if (activeOwner is null)
            {
                return;
            }

            if (!ReferenceEquals(activeOwner.current.Value, this))
            {
                throw new InvalidOperationException("Egress operation scopes must be disposed in order.");
            }

            if (!ReferenceEquals(
                    Interlocked.CompareExchange(ref this.owner, null, activeOwner), activeOwner))
            {
                return;
            }

            activeOwner.current.Value = previous;
        }
    }
}
