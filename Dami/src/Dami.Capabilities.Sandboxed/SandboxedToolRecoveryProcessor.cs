using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Converges durable approved tools into runtime state and journals first activation.</summary>
public sealed class SandboxedToolRecoveryProcessor
{
    private readonly IToolActivationCoordinator coordinator;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IToolActivationRecoverySource source;

    /// <summary>Creates the serialized durable recovery processor.</summary>
    public SandboxedToolRecoveryProcessor(
        IToolActivationRecoverySource source,
        IToolActivationCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coordinator);
        this.source = source;
        this.coordinator = coordinator;
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
            await this.coordinator.ActivateAsync(item, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

}
