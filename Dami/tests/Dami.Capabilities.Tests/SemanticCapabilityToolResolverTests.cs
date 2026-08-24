using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;

namespace Dami.Capabilities.Tests;

public sealed class SemanticCapabilityToolResolverTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 5, 0, 0, TimeSpan.Zero);

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
        var resolver = new SemanticCapabilityToolResolver(
            new StubResolver(selected), schemas);

        var result = await resolver.ResolveAsync(
            "use tools", PrivacyClass.LocalOnly, CancellationToken.None);

        Assert.Equal([firstSchema, secondSchema], result);
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
        public Task<CapabilityBundle> ResolveAsync(
            string intent,
            PrivacyClass privacy,
            CancellationToken cancellationToken) => Task.FromResult(bundle);
    }
}
