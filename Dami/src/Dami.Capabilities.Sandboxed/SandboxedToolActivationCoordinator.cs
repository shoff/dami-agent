using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Owns one exact activation and its durable terminal outcome.</summary>
public sealed class SandboxedToolActivationCoordinator : IToolActivationCoordinator
{
    private readonly IToolActivationStore activationStore;
    private readonly ISandboxedToolActivator activator;
    private readonly TimeProvider clock;

    /// <summary>Creates the one-item activation coordinator.</summary>
    public SandboxedToolActivationCoordinator(
        ISandboxedToolActivator activator,
        IToolActivationStore activationStore,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(activator);
        ArgumentNullException.ThrowIfNull(activationStore);
        ArgumentNullException.ThrowIfNull(clock);
        this.activator = activator;
        this.activationStore = activationStore;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task ActivateAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            await this.activator.ActivateAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (!item.IsActivated)
            {
                await this.RecordFailureAsync(item, exception).ConfigureAwait(false);
            }

            throw;
        }

        if (!item.IsActivated)
        {
            await this.RecordSuccessAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordFailureAsync(
        ToolActivationRecoveryItem item,
        Exception exception)
    {
        var outcome = new ToolActivationOutcome(
            Guid.NewGuid(), item.PromotionId, item.Verification.VerificationId,
            ToolActivationStatus.Failed, exception.GetType().Name, this.clock.GetUtcNow());
        await this.activationStore.RecordAsync(outcome, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task RecordSuccessAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken)
    {
        var outcome = new ToolActivationOutcome(
            Guid.NewGuid(), item.PromotionId, item.Verification.VerificationId,
            ToolActivationStatus.Activated, null, this.clock.GetUtcNow());
        await this.activationStore.RecordAsync(outcome, cancellationToken).ConfigureAwait(false);
    }
}
