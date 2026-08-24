using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;

namespace Dami.Capabilities.Tests;

public sealed class SemanticCapabilitySelectionResolverTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_Should_Preserve_Tools_And_Skill_References_From_One_Bundle()
    {
        var tool = CreateEntry("tool", CapabilityKind.Tool);
        var skill = CreateEntry("skill", CapabilityKind.Skill);
        var schema = CreateSchema(tool);
        var schemas = new CapabilityToolSchemaRegistry();
        schemas.Register(schema);
        var bundleResolver = new StubResolver(new CapabilityBundle("selected", [tool, skill]));
        var resolver = new SemanticCapabilitySelectionResolver(bundleResolver, schemas);

        CapabilitySelection result = await resolver.ResolveAsync(
            "use capabilities", PrivacyClass.LocalOnly, CancellationToken.None);

        Assert.Equal([schema], result.Tools);
        SkillSelection selectedSkill = Assert.Single(result.Skills);
        Assert.Equal(skill.CapabilityId, selectedSkill.CapabilityId);
        Assert.Equal(skill.Name, selectedSkill.Name);
        Assert.Equal(skill.BodyReference, selectedSkill.BodyReference);
        Assert.Equal(skill.Version, selectedSkill.Version);
        Assert.Equal(1, bundleResolver.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_Should_Map_Selected_Tools_To_Typed_Schemas_In_Order()
    {
        var first = CreateEntry("first", CapabilityKind.Tool);
        var skill = CreateEntry("skill", CapabilityKind.Skill);
        var second = CreateEntry("second", CapabilityKind.Tool);
        var firstSchema = CreateSchema(first);
        var secondSchema = CreateSchema(second);
        var schemas = new CapabilityToolSchemaRegistry();
        schemas.Register(secondSchema);
        schemas.Register(firstSchema);
        var selected = new CapabilityBundle("selected", [first, skill, second]);
        var resolver = new SemanticCapabilitySelectionResolver(
            new StubResolver(selected), schemas);

        CapabilitySelection result = await resolver.ResolveAsync(
            "use tools", PrivacyClass.LocalOnly, CancellationToken.None);

        Assert.Equal([firstSchema, secondSchema], result.Tools);
    }

    private static CapabilityEntry CreateEntry(string name, CapabilityKind kind)
    {
        return new CapabilityEntry(
            Guid.NewGuid(), name, $"Use {name}.", kind, CapabilitySource.Native,
            TrustLevel.Trusted, [], kind == CapabilityKind.Tool ? $"native://{name}" : null,
            kind == CapabilityKind.Skill ? $"native://{name}/body" : null, [], "1.0.0", at);
    }

    private static CapabilityToolSchema CreateSchema(CapabilityEntry entry)
    {
        return new CapabilityToolSchema(
            entry.CapabilityId, entry.Name, entry.Description,
            JsonSerializer.SerializeToElement(new { type = "object" }));
    }

    private sealed class StubResolver(CapabilityBundle bundle) : ICapabilityResolver
    {
        public int CallCount { get; private set; }

        public Task<CapabilityBundle> ResolveAsync(
            string intent,
            PrivacyClass privacy,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return Task.FromResult(bundle);
        }
    }
}
