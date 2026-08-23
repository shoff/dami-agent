using System.Collections.Concurrent;

namespace Dami.Capabilities.Tests;

public sealed class CapabilityRegistryTests
{
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
}
