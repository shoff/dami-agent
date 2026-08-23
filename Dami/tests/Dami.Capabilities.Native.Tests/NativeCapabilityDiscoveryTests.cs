namespace Dami.Capabilities.Native.Tests;

public sealed class NativeCapabilityDiscoveryTests
{
    private const string CAPABILITY_ID = "f72b7181-d4d6-4a65-a565-78621d323cca";

    [Fact]
    public void Discover_NormalizesAnnotatedToolWithoutActivatingIt()
    {
        var registeredAt = new DateTimeOffset(2026, 8, 23, 11, 2, 0, TimeSpan.FromHours(-5));
        INativeCapabilityDiscovery discovery = new NativeCapabilityDiscovery();

        var registrations = discovery.Discover(typeof(NativeCapabilityDiscoveryTests).Assembly, registeredAt);

        var registration = Assert.Single(registrations);
        Assert.Equal(typeof(AnnotatedTool), registration.ImplementationType);
        Assert.Equal(Guid.Parse(CAPABILITY_ID), registration.Entry.CapabilityId);
        Assert.Equal("compare-images", registration.Entry.Name);
        Assert.Equal("Compare two images.", registration.Entry.Description);
        Assert.Equal(CapabilityKind.Tool, registration.Entry.Kind);
        Assert.Equal(CapabilitySource.Native, registration.Entry.Source);
        Assert.Equal(TrustLevel.Trusted, registration.Entry.Trust);
        Assert.Equal(["vision", "comparison"], registration.Entry.Tags);
        Assert.Equal("native://compare-images/schema", registration.Entry.SchemaReference);
        Assert.Null(registration.Entry.BodyReference);
        Assert.Empty(registration.Entry.RelatedCapabilities);
        Assert.Equal("1.0.0", registration.Entry.Version);
        Assert.Equal(registeredAt, registration.Entry.RegisteredAt);
        Assert.Equal(0, AnnotatedTool.ConstructionCount);
    }

    [NativeCapability(
        CAPABILITY_ID,
        "compare-images",
        "Compare two images.",
        "native://compare-images/schema",
        "1.0.0",
        Tags = new[] { "vision", "comparison" })]
    private sealed class AnnotatedTool
    {
        public AnnotatedTool()
        {
            ConstructionCount++;
        }

        public static int ConstructionCount { get; private set; }
    }

    private sealed class UnannotatedTool;
}
