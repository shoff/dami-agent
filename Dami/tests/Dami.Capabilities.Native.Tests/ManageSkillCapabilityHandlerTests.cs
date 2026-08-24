using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Native.Tests;

public sealed class ManageSkillCapabilityHandlerTests
{
    private static readonly DateTimeOffset now =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_Should_Translate_Author_Into_A_Trace_Child_Change()
    {
        var relatedId = Guid.NewGuid();
        var skillId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var toolSpanId = Guid.NewGuid();
        var service = new RecordingLifecycleService();
        JsonElement arguments = CreateAuthorArguments(skillId, relatedId);
        var request = new CapabilityExecutionRequest(
            traceId,
            toolSpanId,
            PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn,
            new CapabilityInvocation(Guid.NewGuid(), arguments));

        await new ManageSkillCapabilityHandler(service)
            .ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(
            (traceId, (Guid?)toolSpanId, ExecutionOrigin.UserTurn, SkillChangeKind.Author, skillId,
                "compare-images", "Compare two images consistently.",
                "Inspect geometry before color.", "vision|comparison", relatedId,
                "checklist.md=Check scale.", true),
            Describe(service.Request!, toolSpanId));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Translate_Revise_With_The_Expected_Preimage()
    {
        var skillId = Guid.NewGuid();
        string expectedVersion = new('b', 64);
        var service = new RecordingLifecycleService();
        var request = CreateRequest(CreateReviseArguments(skillId, expectedVersion));

        CapabilityExecutionResult result = await new ManageSkillCapabilityHandler(service)
            .ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(
            (SkillChangeKind.Revise, skillId, expectedVersion, "Rework the comparison order.",
                "Use geometry, lighting, then color.", "revise", "true"),
            (service.Request!.Kind, service.Request.SkillId, service.Request.ExpectedVersion,
                service.Diff, service.Request.Replacement!.Body,
                result.Evidence["operation"], result.Evidence["materialized"]));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Translate_Retire_Without_A_Replacement()
    {
        var skillId = Guid.NewGuid();
        string expectedVersion = new('c', 64);
        var service = new RecordingLifecycleService();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "retire",
            skillId,
            expectedVersion,
            diff = "Retire the obsolete comparison procedure.",
        });

        CapabilityExecutionResult result = await new ManageSkillCapabilityHandler(service)
            .ExecuteAsync(CreateRequest(arguments), CancellationToken.None);

        Assert.Equal(
            (SkillChangeKind.Retire, expectedVersion, true,
                "Retire the obsolete comparison procedure.", "retire", "true", false),
            (service.Request!.Kind, service.Request.ExpectedVersion,
                service.Request.Replacement is null, service.Diff,
                result.Evidence["operation"], result.Evidence["materialized"],
                result.Evidence.ContainsKey("replacement_version")));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Derive_Stable_Change_And_Child_Span_Ids_For_Retry()
    {
        var service = new RecordingLifecycleService();
        var request = CreateRequest(CreateAuthorArguments(Guid.NewGuid(), Guid.NewGuid()));
        var handler = new ManageSkillCapabilityHandler(service);

        await handler.ExecuteAsync(request, CancellationToken.None);
        await handler.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(
            (service.Requests[0].ChangeId, service.Requests[0].SpanId),
            (service.Requests[1].ChangeId, service.Requests[1].SpanId));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Default_Omitted_Collections_To_Empty()
    {
        var service = new RecordingLifecycleService();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            operation = "author",
            skillId = Guid.NewGuid(),
            name = "minimal-skill",
            description = "A minimal authored skill.",
            body = "Use the smallest valid input.",
            diff = "Author minimal skill.",
        });

        await new ManageSkillCapabilityHandler(service)
            .ExecuteAsync(CreateRequest(arguments), CancellationToken.None);

        Assert.Equal(
            (0, 0, 0),
            (service.Request!.Replacement!.Tags.Count,
                service.Request.Replacement.RelatedCapabilities.Count,
                service.Request.Replacement.References.Count));
    }

    [Fact]
    public void Discovery_Should_Advertise_The_Trusted_Skill_Lifecycle_Tool()
    {
        NativeCapabilityRegistration registration = new NativeCapabilityDiscovery().Discover(
            typeof(ManageSkillCapabilityHandler).Assembly, now)
            .Single(item => item.ImplementationType == typeof(ManageSkillCapabilityHandler));

        Assert.Equal(
            ("manage-skill", CapabilitySource.Native, TrustLevel.Trusted,
                "native://manage-skill/schema/v1", "skills|authoring|procedure"),
            (registration.Entry.Name, registration.Entry.Source, registration.Entry.Trust,
                registration.Entry.SchemaReference, string.Join('|', registration.Entry.Tags)));
    }

    [Fact]
    public void Constructor_Should_Reject_Null_Lifecycle_Service()
    {
        Assert.Throws<ArgumentNullException>(() => new ManageSkillCapabilityHandler(null!));
    }

    private static JsonElement CreateAuthorArguments(Guid skillId, Guid relatedId)
    {
        return JsonSerializer.SerializeToElement(new
        {
            operation = "author",
            skillId,
            name = "compare-images",
            description = "Compare two images consistently.",
            body = "Inspect geometry before color.",
            tags = new[] { "vision", "comparison" },
            relatedCapabilities = new[] { relatedId },
            references = new Dictionary<string, string> { ["checklist.md"] = "Check scale." },
            diff = "Author compare-images skill.",
        });
    }

    private static JsonElement CreateReviseArguments(Guid skillId, string expectedVersion)
    {
        return JsonSerializer.SerializeToElement(new
        {
            operation = "revise",
            skillId,
            expectedVersion,
            name = "compare-images",
            description = "Compare two images consistently.",
            body = "Use geometry, lighting, then color.",
            tags = new[] { "vision" },
            relatedCapabilities = Array.Empty<Guid>(),
            references = new Dictionary<string, string>(),
            diff = "Rework the comparison order.",
        });
    }

    private static CapabilityExecutionRequest CreateRequest(JsonElement arguments)
    {
        return new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(Guid.NewGuid(), arguments));
    }

    private static object Describe(SkillChangeRequest change, Guid toolSpanId)
    {
        KeyValuePair<string, string> reference = change.Replacement!.References.Single();
        return (change.TraceId, change.ParentSpanId, change.Origin, change.Kind, change.SkillId,
            change.Replacement.Name, change.Replacement.Description, change.Replacement.Body,
            string.Join('|', change.Replacement.Tags), change.Replacement.RelatedCapabilities[0],
            $"{reference.Key}={reference.Value}", change.SpanId != toolSpanId);
    }

    private sealed class RecordingLifecycleService : ISkillLifecycleService
    {
        public List<SkillChangeRequest> Requests { get; } = [];

        public SkillChangeRequest? Request { get; private set; }

        public string? Diff { get; private set; }

        public Task<SkillChangeRecord> ApplyAsync(
            SkillChangeRequest request,
            string diff,
            CancellationToken cancellationToken)
        {
            this.Request = request;
            this.Requests.Add(request);
            this.Diff = diff;
            return Task.FromResult(new SkillChangeRecord(
                request,
                diff,
                request.Replacement is null ? null : new string('a', 64),
                now));
        }
    }
}
