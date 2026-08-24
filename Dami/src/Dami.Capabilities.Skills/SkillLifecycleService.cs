using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills;

/// <summary>Orders durable write-ahead before skill materialization.</summary>
public sealed class SkillLifecycleService : ISkillLifecycleService
{
    private readonly TimeProvider clock;
    private readonly ISkillChangeProcessor processor;
    private readonly ISkillChangeStore store;
    private readonly SkillDocumentVersioner versioner;

    /// <summary>Creates the lifecycle application service.</summary>
    public SkillLifecycleService(
        ISkillChangeStore store,
        ISkillChangeProcessor processor,
        SkillDocumentVersioner versioner,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(versioner);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.processor = processor;
        this.versioner = versioner;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async Task<SkillChangeRecord> ApplyAsync(
        SkillChangeRequest request,
        string diff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? replacementVersion = request.Replacement is null
            ? null
            : this.versioner.Compute(request.Replacement);
        var record = new SkillChangeRecord(
            request, diff, replacementVersion, this.clock.GetUtcNow());
        SkillChangeRecord accepted = await this.store
            .CreateAsync(record, cancellationToken).ConfigureAwait(false);
        await this.processor.ProcessAsync(accepted, cancellationToken).ConfigureAwait(false);
        return accepted;
    }
}
