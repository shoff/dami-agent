using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills;

/// <summary>Serializes materialization, registry reload, verification, and outcomes.</summary>
public sealed class SkillChangeRecoveryProcessor : ISkillChangeProcessor
{
    private readonly ICapabilityCatalog catalog;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISkillChangeMaterializer materializer;
    private readonly ISkillSourceReloader reloader;
    private readonly ISkillChangeRecoveryStore store;

    /// <summary>Creates the crash-recovery processor.</summary>
    public SkillChangeRecoveryProcessor(
        ISkillChangeRecoveryStore store,
        ISkillChangeMaterializer materializer,
        ISkillSourceReloader reloader,
        ICapabilityCatalog catalog,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(materializer);
        ArgumentNullException.ThrowIfNull(reloader);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.materializer = materializer;
        this.reloader = reloader;
        this.catalog = catalog;
        this.clock = clock;
    }

    /// <summary>Retries one bounded batch of changes lacking durable success.</summary>
    public async Task<SkillRecoverySummary> RecoverAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SkillChangeRecord> pending = await this.store
            .FindPendingAsync(limit, cancellationToken).ConfigureAwait(false);
        var succeeded = 0;
        var failed = 0;
        for (var index = 0; index < pending.Count; index++)
        {
            try
            {
                await this.ProcessAsync(pending[index], cancellationToken).ConfigureAwait(false);
                succeeded++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
            }
        }

        return new SkillRecoverySummary(pending.Count, succeeded, failed);
    }

    /// <summary>Processes one durable change and records its observed outcome.</summary>
    public async Task ProcessAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await this.store.IsPendingAsync(
                record.Request.ChangeId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await this.ApplyWithFailureEventAsync(record, cancellationToken).ConfigureAwait(false);
            await this.store.RecordSucceededAsync(
                record, this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task ApplyWithFailureEventAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.ApplyAndVerifyAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await this.RecordFailureAsync(record, exception).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ApplyAndVerifyAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        await this.materializer.ApplyAsync(record, cancellationToken).ConfigureAwait(false);
        DateTimeOffset reloadedAt = this.clock.GetUtcNow();
        await this.reloader.ReloadAsync(reloadedAt, cancellationToken).ConfigureAwait(false);
        this.EnsurePublished(record);
    }

    private void EnsurePublished(SkillChangeRecord record)
    {
        CapabilityEntry? published = this.catalog.Find(record.Request.SkillId);
        bool expected = record.Request.Kind == SkillChangeKind.Retire
            ? published is null
            : string.Equals(
                published?.Version, record.ReplacementVersion, StringComparison.Ordinal);
        if (!expected)
        {
            throw new InvalidDataException(
                "Reloaded skill registry does not match the materialized postcondition.");
        }
    }

    private async Task RecordFailureAsync(SkillChangeRecord record, Exception exception)
    {
        await this.store.RecordFailedAsync(
            record, exception.GetType().Name, this.clock.GetUtcNow(), CancellationToken.None)
            .ConfigureAwait(false);
    }
}
