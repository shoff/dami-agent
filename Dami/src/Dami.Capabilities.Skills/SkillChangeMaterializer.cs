using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Skills;

/// <summary>Converges write-ahead skill changes into complete filesystem directories.</summary>
public sealed class SkillChangeMaterializer : ISkillChangeMaterializer
{
    private readonly SkillDocumentVersioner versioner;
    private readonly SkillDocumentWriter writer;
    private readonly SkillDirectoryLocator locator;
    private readonly string rootDirectory;

    /// <summary>Creates a bounded same-filesystem materializer.</summary>
    public SkillChangeMaterializer(
        SkillLoaderOptions options,
        SkillDocumentVersioner versioner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(versioner);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootDirectory);
        this.rootDirectory = Path.GetFullPath(options.RootDirectory);
        this.locator = new SkillDirectoryLocator(options);
        this.writer = new SkillDocumentWriter(options);
        this.versioner = versioner;
    }

    /// <summary>Idempotently applies one already-durable skill change.</summary>
    public async Task ApplyAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureOrdinaryRoot(this.rootDirectory);
        switch (record.Request.Kind)
        {
            case SkillChangeKind.Author:
                await this.ApplyAuthorAsync(record, cancellationToken).ConfigureAwait(false);
                return;
            case SkillChangeKind.Revise:
                await this.ApplyRevisionAsync(record, cancellationToken).ConfigureAwait(false);
                return;
            case SkillChangeKind.Retire:
                await this.ApplyRetirementAsync(record, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(record));
        }
    }

    private async Task ApplyAuthorAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        SkillDocument document = record.Request.Replacement!;
        this.EnsureVersion(record, document);
        string target = Path.Combine(this.rootDirectory, document.SkillId.ToString("D"));
        string stage = StagePath(this.rootDirectory, record.Request.ChangeId);
        if (TargetHasVersion(target, record.ReplacementVersion!))
        {
            await this.StageAndExchangeAsync(
                record, document, stage, target, cancellationToken).ConfigureAwait(false);
            return;
        }

        SkillDirectoryIdentity? existing = await this.locator
            .FindAsync(document.SkillId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidDataException("A different skill revision already occupies the target.");
        }

        this.locator.EnsureCapacityForNew();
        await this.StageAndMoveAsync(record, document, stage, target, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyRevisionAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        SkillDocument document = record.Request.Replacement!;
        this.EnsureVersion(record, document);
        string stage = StagePath(this.rootDirectory, record.Request.ChangeId);
        SkillDirectoryIdentity? existing = await this.locator
            .FindAsync(document.SkillId, cancellationToken).ConfigureAwait(false);
        if (existing is not null
            && TargetHasVersion(existing.Directory, record.ReplacementVersion!))
        {
            await this.StageAndExchangeAsync(
                record, document, stage, existing.Directory, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (existing?.Version == record.ReplacementVersion)
        {
            DeleteDirectory(stage);
            return;
        }

        EnsurePreimage(existing, document.SkillId, record.Request.ExpectedVersion!);
        await this.StageAndExchangeAsync(
            record, document, stage, existing!.Directory, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyRetirementAsync(
        SkillChangeRecord record,
        CancellationToken cancellationToken)
    {
        string retired = Path.Combine(
            this.rootDirectory, $".dami-retire-{record.Request.ChangeId:N}");
        string tombstone = Path.Combine(
            this.rootDirectory, $".dami-retirement-{record.Request.ChangeId:N}");
        if (TombstoneHasVersion(tombstone, record.Request.ExpectedVersion!))
        {
            DeleteDirectory(retired);
            return;
        }

        await this.FinishRetirementAsync(record, retired, tombstone, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task FinishRetirementAsync(
        SkillChangeRecord record,
        string retired,
        string tombstone,
        CancellationToken cancellationToken)
    {
        string expectedVersion = record.Request.ExpectedVersion!;
        if (!Directory.Exists(retired))
        {
            SkillDirectoryIdentity? existing = await this.locator
                .FindAsync(record.Request.SkillId, cancellationToken).ConfigureAwait(false);
            EnsurePreimage(existing, record.Request.SkillId, expectedVersion);
            Directory.Move(existing!.Directory, retired);
        }

        SkillDirectoryIdentity moved = await this.locator
            .InspectAsync(retired, cancellationToken).ConfigureAwait(false);
        EnsurePreimage(moved, record.Request.SkillId, expectedVersion);
        await SkillDocumentWriter.WriteDurableTextAsync(
            tombstone, expectedVersion, cancellationToken).ConfigureAwait(false);
        DeleteDirectory(retired);
    }

    private async Task StageAndMoveAsync(
        SkillChangeRecord record,
        SkillDocument document,
        string stage,
        string target,
        CancellationToken cancellationToken)
    {
        try
        {
            DeleteDirectory(stage);
            await this.writer.WriteAsync(stage, document, cancellationToken).ConfigureAwait(false);
            await SkillDocumentWriter.WriteVersionAsync(
                stage, record.ReplacementVersion!, cancellationToken).ConfigureAwait(false);
            Directory.Move(stage, target);
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                DeleteDirectory(stage);
            }
        }
    }

    private async Task StageAndExchangeAsync(
        SkillChangeRecord record,
        SkillDocument document,
        string stage,
        string target,
        CancellationToken cancellationToken)
    {
        try
        {
            DeleteDirectory(stage);
            await this.writer.WriteAsync(stage, document, cancellationToken).ConfigureAwait(false);
            await SkillDocumentWriter.WriteVersionAsync(
                stage, record.ReplacementVersion!, cancellationToken).ConfigureAwait(false);
            AtomicDirectoryExchange.Exchange(stage, target);
        }
        finally
        {
            DeleteDirectory(stage);
        }
    }

    private static bool TargetHasVersion(string target, string expectedVersion)
    {
        if (!Directory.Exists(target))
        {
            return false;
        }

        string marker = Path.Combine(target, SkillDocumentWriter.VERSION_FILE);
        return File.Exists(marker)
            && string.Equals(File.ReadAllText(marker), expectedVersion, StringComparison.Ordinal);
    }

    private static bool TombstoneHasVersion(string path, string expectedVersion)
    {
        return File.Exists(path)
            && string.Equals(File.ReadAllText(path), expectedVersion, StringComparison.Ordinal);
    }

    private static void EnsurePreimage(
        SkillDirectoryIdentity? existing,
        Guid skillId,
        string expectedVersion)
    {
        if (existing?.SkillId != skillId
            || !string.Equals(existing.Version, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Skill preimage no longer matches the expected version.");
        }
    }

    private static string StagePath(string rootDirectory, Guid changeId)
    {
        return Path.Combine(rootDirectory, $".dami-stage-{changeId:N}");
    }

    private static void EnsureOrdinaryRoot(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Skill root '{rootDirectory}' does not exist.");
        }

        if ((File.GetAttributes(rootDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Symbolic-link skill roots are not allowed.");
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void EnsureVersion(SkillChangeRecord record, SkillDocument document)
    {
        string computed = this.versioner.Compute(document);
        if (!string.Equals(computed, record.ReplacementVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Skill replacement version does not match its document.");
        }
    }
}
