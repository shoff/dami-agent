using Dami.Contracts.Context;

namespace Dami.Capabilities.Tests;

public sealed class CapabilityBundleExpanderTests
{
    [Fact]
    public void Expand_IncludesSelectedSkillAndItsReferencedTool()
    {
        var tool = CreateTool(Guid.NewGuid());
        var skill = CreateSkill(Guid.NewGuid(), [tool.CapabilityId]);
        var registry = new CapabilityRegistry();
        registry.Register(tool);
        registry.Register(skill);
        ICapabilityBundleExpander expander = new CapabilityBundleExpander(registry);

        var bundle = expander.Expand(
            "image-comparison", [skill.CapabilityId], PrivacyClass.LocalOnly);

        Assert.Equal("image-comparison", bundle.Name);
        Assert.Equal([skill, tool], bundle.Capabilities);
    }

    [Fact]
    public void Expand_IncludesSharedReferencesOnlyOnce()
    {
        var tool = CreateTool(Guid.NewGuid());
        var firstSkill = CreateSkill(Guid.NewGuid(), [tool.CapabilityId]);
        var secondSkill = CreateSkill(Guid.NewGuid(), [tool.CapabilityId]);
        var registry = new CapabilityRegistry();
        registry.Register(tool);
        registry.Register(firstSkill);
        registry.Register(secondSkill);
        var expander = new CapabilityBundleExpander(registry);

        var bundle = expander.Expand(
            "shared-tool",
            [firstSkill.CapabilityId, secondSkill.CapabilityId],
            PrivacyClass.LocalOnly);

        Assert.Equal([firstSkill, tool, secondSkill], bundle.Capabilities);
    }

    [Fact]
    public void Expand_RecursesThroughBundleAndTerminatesOnCycle()
    {
        var bundleId = Guid.NewGuid();
        var tool = CreateTool(Guid.NewGuid());
        var skill = CreateSkill(Guid.NewGuid(), [bundleId, tool.CapabilityId]);
        var bundleEntry = CreateBundle(bundleId, [skill.CapabilityId]);
        var registry = new CapabilityRegistry();
        registry.Register(tool);
        registry.Register(skill);
        registry.Register(bundleEntry);
        var expander = new CapabilityBundleExpander(registry);

        var bundle = expander.Expand(
            "cyclic-bundle", [bundleEntry.CapabilityId], PrivacyClass.LocalOnly);

        Assert.Equal([skill, tool], bundle.Capabilities);
    }

    [Fact]
    public void Expand_ReportsReferrerWhenRelatedCapabilityIsMissing()
    {
        var missingCapabilityId = Guid.NewGuid();
        var skill = CreateSkill(Guid.NewGuid(), [missingCapabilityId]);
        var registry = new CapabilityRegistry();
        registry.Register(skill);
        var expander = new CapabilityBundleExpander(registry);

        var exception = Assert.Throws<KeyNotFoundException>(
            () => expander.Expand(
                "missing-reference", [skill.CapabilityId], PrivacyClass.LocalOnly));

        Assert.Contains(missingCapabilityId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(skill.CapabilityId.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityBundle_RejectsBundleDefinitionAsTurnContent()
    {
        var bundleEntry = CreateBundle(Guid.NewGuid(), []);

        var exception = Assert.Throws<ArgumentException>(
            () => new CapabilityBundle("nested-bundle", [bundleEntry]));

        Assert.Equal("capabilities", exception.ParamName);
    }

    private static CapabilityEntry CreateTool(Guid capabilityId)
    {
        return new CapabilityEntry(
            capabilityId,
            "compare-images",
            "Compare two images.",
            CapabilityKind.Tool,
            CapabilitySource.Native,
            TrustLevel.Trusted,
            ["vision"],
            "native://compare-images/schema",
            null,
            [],
            "1.0.0",
            new DateTimeOffset(2026, 8, 23, 10, 51, 0, TimeSpan.FromHours(-5)));
    }

    private static CapabilityEntry CreateSkill(
        Guid capabilityId,
        IReadOnlyList<Guid> relatedCapabilities)
    {
        return new CapabilityEntry(
            capabilityId,
            "image-comparison-procedure",
            "Procedure for comparing images.",
            CapabilityKind.Skill,
            CapabilitySource.Skill,
            TrustLevel.Trusted,
            ["vision"],
            null,
            "skill://image-comparison/SKILL.md",
            relatedCapabilities,
            "1.0.0",
            new DateTimeOffset(2026, 8, 23, 10, 51, 0, TimeSpan.FromHours(-5)));
    }

    private static CapabilityEntry CreateBundle(
        Guid capabilityId,
        IReadOnlyList<Guid> relatedCapabilities)
    {
        return new CapabilityEntry(
            capabilityId,
            "image-capabilities",
            "Tools and procedures for images.",
            CapabilityKind.Bundle,
            CapabilitySource.Native,
            TrustLevel.Trusted,
            ["vision"],
            null,
            null,
            relatedCapabilities,
            "1.0.0",
            new DateTimeOffset(2026, 8, 23, 10, 51, 0, TimeSpan.FromHours(-5)));
    }
}
