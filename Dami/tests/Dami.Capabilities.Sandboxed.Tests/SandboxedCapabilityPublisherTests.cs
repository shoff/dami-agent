using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxedCapabilityPublisherTests
{
    [Fact]
    public void Publish_Should_Roll_Back_New_Dependencies_When_Metadata_Is_Duplicate()
    {
        var capabilityId = Guid.NewGuid();
        var handlers = new SandboxedCapabilityRegistry();
        var schemas = new CapabilityToolSchemaRegistry();
        var capabilities = new CapabilityRegistry();
        var existing = CreateEntry(capabilityId, "existing");
        capabilities.Register(existing);
        var publisher = new SandboxedCapabilityPublisher(handlers, schemas, capabilities);
        var registration = CreateRegistration(capabilityId);
        var schema = CreateSchema(capabilityId);

        Assert.Throws<InvalidOperationException>(() => publisher.Publish(
            registration, schema, CreateEntry(capabilityId, registration.ArtifactVersion)));

        Assert.Null(handlers.Find(capabilityId));
        Assert.Null(schemas.Find(capabilityId));
        Assert.Same(existing, capabilities.Find(capabilityId));
    }

    private static SandboxedCapabilityRegistration CreateRegistration(Guid capabilityId)
    {
        return new SandboxedCapabilityRegistration(
            capabilityId,
            new ToolVerificationRecord(
                Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), new string('b', 64),
                "1 proposal test passed",
                new DateTimeOffset(2026, 8, 24, 23, 44, 0, TimeSpan.Zero)),
            Path.Combine(Path.GetTempPath(), capabilityId.ToString("N")));
    }

    private static CapabilityToolSchema CreateSchema(Guid capabilityId)
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        return new CapabilityToolSchema(
            capabilityId, "sandboxed-tool", "Run a sandboxed tool.", parameters.RootElement);
    }

    private static CapabilityEntry CreateEntry(Guid capabilityId, string version)
    {
        return new CapabilityEntry(
            capabilityId, "sandboxed-tool", "Run a sandboxed tool.", CapabilityKind.Tool,
            CapabilitySource.Sandboxed, TrustLevel.Trusted, ["sandboxed"],
            $"sandboxed://{capabilityId:D}/schema", null, [], version,
            new DateTimeOffset(2026, 8, 24, 23, 45, 0, TimeSpan.Zero));
    }
}
