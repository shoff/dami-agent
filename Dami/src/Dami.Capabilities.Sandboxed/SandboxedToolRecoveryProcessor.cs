using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Converges durable approved tools into runtime state and journals first activation.</summary>
public sealed class SandboxedToolRecoveryProcessor
{
    private readonly IToolActivationStore activationStore;
    private readonly ISandboxedToolActivator activator;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IToolActivationRecoverySource source;

    /// <summary>Creates the serialized durable recovery processor.</summary>
    public SandboxedToolRecoveryProcessor(
        IToolActivationRecoverySource source,
        ISandboxedToolActivator activator,
        IToolActivationStore activationStore,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(activator);
        ArgumentNullException.ThrowIfNull(activationStore);
        ArgumentNullException.ThrowIfNull(clock);
        this.source = source;
        this.activator = activator;
        this.activationStore = activationStore;
        this.clock = clock;
    }

    /// <summary>Processes one bounded deterministic startup batch.</summary>
    public async Task<ToolActivationRecoverySummary> RecoverAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await this.RecoverCoreAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task<ToolActivationRecoverySummary> RecoverCoreAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ToolActivationRecoveryItem> items = await this.source
            .FindAsync(limit, cancellationToken).ConfigureAwait(false);
        var succeeded = 0;
        var failed = 0;
        for (var index = 0; index < items.Count; index++)
        {
            if (await this.TryRecoverAsync(items[index], cancellationToken).ConfigureAwait(false))
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        return new ToolActivationRecoverySummary(items.Count, succeeded, failed);
    }

    private async Task<bool> TryRecoverAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.ActivateWithFailureAsync(item, cancellationToken).ConfigureAwait(false);
            if (!item.IsActivated)
            {
                await this.RecordSuccessAsync(item, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task ActivateWithFailureAsync(
        ToolActivationRecoveryItem item,
        CancellationToken cancellationToken)
    {
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
