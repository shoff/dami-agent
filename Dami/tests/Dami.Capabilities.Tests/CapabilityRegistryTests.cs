using System.Collections.Concurrent;

namespace Dami.Capabilities.Tests;

public sealed class CapabilityRegistryTests
{
    [Fact]
    public void Snapshot_Should_Return_A_Deterministic_Point_In_Time()
    {
        var first = CreateEntry(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = CreateEntry(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var third = CreateEntry(Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var registry = new CapabilityRegistry();
        registry.Register(second);
        registry.Register(first);
        ICapabilityInventory inventory = registry;

        IReadOnlyList<CapabilityEntry> snapshot = inventory.Snapshot();
        registry.Register(third);

        Assert.Equal([first, second], snapshot);
        Assert.Equal([first, second, third], inventory.Snapshot());
    }

    [Fact]
    public void Register_Should_Support_Concurrent_Registration_And_Lookup()
    {
        const int registrationCount = 20_000;
        CapabilityEntry[] entries = Enumerable
            .Range(0, registrationCount)
            .Select(index => CreateEntry(Guid.NewGuid(), name: $"capability-{index}"))
            .ToArray();
        var registry = new CapabilityRegistry();

        Parallel.ForEach(entries, registry.Register);
        Parallel.ForEach(entries, entry => Assert.Same(entry, registry.Find(entry.CapabilityId)));
    }

    [Fact]
    public void Register_Should_Atomically_Reject_Concurrent_Duplicates()
    {
        const int registrationCount = 1_000;
        var capabilityId = Guid.NewGuid();
        CapabilityEntry[] entries = Enumerable
            .Range(0, registrationCount)
            .Select(index => CreateEntry(capabilityId, name: $"candidate-{index}"))
            .ToArray();
        var failures = new ConcurrentBag<Exception>();
        var registry = new CapabilityRegistry();

        Parallel.ForEach(entries, entry => RecordFailure(() => registry.Register(entry), failures));

        Assert.Equal(registrationCount - 1, failures.Count);
        Assert.All(failures, failure =>
        {
            var duplicate = Assert.IsType<InvalidOperationException>(failure);
            Assert.Contains(capabilityId.ToString(), duplicate.Message, StringComparison.Ordinal);
        });
        Assert.Contains(registry.Find(capabilityId), entries);
    }

    [Fact]
    public void RegisterBatch_Should_Publish_One_Atomic_Snapshot_To_Readers()
    {
        var first = CreateEntry(Guid.NewGuid());
        var second = CreateEntry(Guid.NewGuid());
        var registry = new CapabilityRegistry();
        var observedCounts = new List<int>();
        var batch = new ObservingBatch(
            [first, second],
            () => observedCounts.Add(registry.Snapshot().Count));
        ICapabilityBatchRegistrar registrar = registry;

        registrar.RegisterBatch(batch);

        Assert.All(observedCounts, count => Assert.Equal(0, count));
        Assert.Equal(2, registry.Snapshot().Count);
    }

    [Fact]
    public void ReplaceSourceSnapshot_Should_Atomically_Revise_Only_That_Source()
    {
        var skillId = Guid.NewGuid();
        var native = CreateEntry(Guid.NewGuid());
        var original = CreateEntry(
            skillId, kind: CapabilityKind.Skill, source: CapabilitySource.Skill,
            schemaReference: null, bodyReference: "skill://body", version: "version-1");
        var revised = CreateEntry(
            skillId, kind: CapabilityKind.Skill, source: CapabilitySource.Skill,
            schemaReference: null, bodyReference: "skill://body", version: "version-2");
        var registry = new CapabilityRegistry();
        registry.Register(native);
        registry.Register(original);
        var observedVersions = new List<string?>();
        var replacement = new ObservingBatch(
            [revised],
            () => observedVersions.Add(registry.Find(skillId)?.Version));
        ICapabilitySourceSnapshotRegistrar registrar = registry;

        registrar.ReplaceSourceSnapshot(CapabilitySource.Skill, replacement);

        Assert.All(observedVersions, version => Assert.Equal("version-1", version));
        Assert.Same(native, registry.Find(native.CapabilityId));
        Assert.Same(revised, registry.Find(skillId));
    }

    [Fact]
    public void Register_MakesCapabilityAvailableByStableId()
    {
        var capabilityId = Guid.NewGuid();
        var entry = CreateEntry(capabilityId);
        var registry = new CapabilityRegistry();
        ICapabilityRegistrar registrar = registry;
        ICapabilityCatalog catalog = registry;

        registrar.Register(entry);

        Assert.Equal(entry, catalog.Find(capabilityId));
    }

    [Fact]
    public void Register_RejectsDuplicateStableIdWithoutReplacingOriginal()
    {
        var capabilityId = Guid.NewGuid();
        var original = CreateEntry(capabilityId);
        var replacement = CreateEntry(capabilityId, name: "replacement");
        var registry = new CapabilityRegistry();
        registry.Register(original);

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(replacement));

        Assert.Contains(capabilityId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, registry.Find(capabilityId));
    }

    [Fact]
    public void CapabilityEntry_SnapshotsCollectionMetadata()
    {
        var relatedCapabilityId = Guid.NewGuid();
        var tags = new List<string> { "vision" };
        var relatedCapabilities = new List<Guid> { relatedCapabilityId };

        var entry = CreateEntry(Guid.NewGuid(), tags, relatedCapabilities);
        tags.Add("mutated-after-registration");
        relatedCapabilities.Clear();

        Assert.Equal(["vision"], entry.Tags);
        Assert.Equal([relatedCapabilityId], entry.RelatedCapabilities);
    }

    [Fact]
    public void CapabilityEntry_RejectsToolWithoutTypedSchemaReference()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateEntry(Guid.NewGuid(), schemaReference: null));

        Assert.Equal("schemaReference", exception.ParamName);
    }

    [Fact]
    public void CapabilityEntry_RejectsSkillWithoutBodyReference()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateEntry(
                Guid.NewGuid(),
                kind: CapabilityKind.Skill,
                source: CapabilitySource.Skill,
                schemaReference: null));

        Assert.Equal("bodyReference", exception.ParamName);
    }

    [Theory]
    [InlineData(CapabilityKind.Tool, "native://tool/schema", "skill://body", "bodyReference")]
    [InlineData(CapabilityKind.Skill, "native://tool/schema", "skill://body", "schemaReference")]
    [InlineData(CapabilityKind.Bundle, "native://tool/schema", null, "schemaReference")]
    [InlineData(CapabilityKind.Bundle, null, "skill://body", "bodyReference")]
    public void CapabilityEntry_RejectsReferencesOwnedByAnotherKind(
        CapabilityKind kind,
        string? schemaReference,
        string? bodyReference,
        string expectedParameter)
    {
        var source = kind == CapabilityKind.Skill ? CapabilitySource.Skill : CapabilitySource.Native;

        var exception = Assert.Throws<ArgumentException>(
            () => CreateEntry(
                Guid.NewGuid(),
                kind: kind,
                source: source,
                schemaReference: schemaReference,
                bodyReference: bodyReference));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public void CapabilityEntry_RejectsEmptyStableId()
    {
        var exception = Assert.Throws<ArgumentException>(() => CreateEntry(Guid.Empty));

        Assert.Equal("capabilityId", exception.ParamName);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("version")]
    public void CapabilityEntry_RejectsMissingRequiredText(string missingParameter)
    {
        var name = missingParameter == "name" ? " " : "compare-images";
        var description = missingParameter == "description" ? " " : "Compare two images.";
        var version = missingParameter == "version" ? " " : "1.0.0";

        var exception = Assert.Throws<ArgumentException>(
            () => CreateEntry(
                Guid.NewGuid(),
                name: name,
                description: description,
                version: version));

        Assert.Equal(missingParameter, exception.ParamName);
    }

    private static CapabilityEntry CreateEntry(
        Guid capabilityId,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<Guid>? relatedCapabilities = null,
        CapabilityKind kind = CapabilityKind.Tool,
        CapabilitySource source = CapabilitySource.Native,
        string? schemaReference = "native://compare-images/schema",
        string? bodyReference = null,
        string name = "compare-images",
        string description = "Compare two images and describe their differences.",
        string version = "1.0.0")
    {
        return new CapabilityEntry(
            capabilityId,
            name,
            description,
            kind,
            source,
            TrustLevel.Trusted,
            tags ?? ["vision", "comparison"],
            schemaReference,
            bodyReference,
            relatedCapabilities ?? [],
            version,
            new DateTimeOffset(2026, 8, 23, 10, 33, 0, TimeSpan.FromHours(-5)));
    }

    private static void RecordFailure(Action action, ConcurrentBag<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private sealed class ObservingBatch(
        IReadOnlyList<CapabilityEntry> entries,
        Action observe) : IReadOnlyList<CapabilityEntry>
    {
        public int Count => entries.Count;

        public CapabilityEntry this[int index]
        {
            get
            {
                observe();
                return entries[index];
            }
        }

        public IEnumerator<CapabilityEntry> GetEnumerator()
        {
            return entries.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
