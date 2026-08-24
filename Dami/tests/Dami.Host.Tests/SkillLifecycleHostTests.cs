using System.Text.Json;
using Dami.Capabilities;
using Dami.Capabilities.Skills;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dami.Host.Tests;

public sealed class SkillLifecycleHostTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "dami-host-skill-live-" + Guid.NewGuid().ToString("N")))
        .FullName;

    public void Dispose()
    {
        Directory.Delete(this.root, recursive: true);
    }

    [Fact]
    public async Task Native_Executor_Should_Author_Revise_And_Retire_A_Live_Skill()
    {
        var skillId = Guid.NewGuid();
        var store = new InMemorySkillChangeStore();
        await using WebApplicationFactory<Program> factory = CreateFactory(this.root, store);
        using var client = factory.CreateClient();
        using HttpResponseMessage health = await client.GetAsync("/health", CancellationToken.None);
        Guid capabilityId = FindManageSkill(factory.Services).CapabilityId;

        CapabilityExecutionResult authored = await InvokeAsync(
            factory.Services, capabilityId, AuthorArguments(skillId));
        string firstBody = await ReadBodyAsync(factory.Services, skillId, authored);
        CapabilityExecutionResult revised = await InvokeAsync(
            factory.Services, capabilityId,
            ReviseArguments(skillId, authored.Evidence["replacement_version"]));
        string secondBody = await ReadBodyAsync(factory.Services, skillId, revised);
        await InvokeAsync(
            factory.Services, capabilityId,
            RetireArguments(skillId, revised.Evidence["replacement_version"]));

        Assert.Equal(
            ("Start with geometry.", "Finish with lighting.", true, 3),
            (firstBody, secondBody,
                factory.Services.GetRequiredService<ICapabilityCatalog>().Find(skillId) is null,
                store.SucceededCount));
    }

    [Fact]
    public async Task Host_Startup_Should_Recover_A_Durable_Unmaterialized_Change()
    {
        var skillId = Guid.NewGuid();
        var document = new SkillDocument(
            skillId, "recovered-skill", "Recovered after a crash.", "Recovery body.",
            ["recovery"], Array.Empty<Guid>(), new Dictionary<string, string>());
        string version = new SkillDocumentVersioner().Compute(document);
        var request = new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, SkillChangeKind.Author, skillId, null, document);
        var store = new InMemorySkillChangeStore(
            new SkillChangeRecord(request, "recover authored skill", version, DateTimeOffset.UnixEpoch));

        await using WebApplicationFactory<Program> factory = CreateFactory(this.root, store);
        using var client = factory.CreateClient();
        using HttpResponseMessage health = await client.GetAsync("/health", CancellationToken.None);
        string body = await factory.Services.GetRequiredService<ISkillContentReader>()
            .ReadBodyAsync(skillId, version, CancellationToken.None);

        Assert.Equal(
            ("Recovery body.", version, 1),
            (body, factory.Services.GetRequiredService<ICapabilityCatalog>()
                .Find(skillId)!.Version, store.SucceededCount));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string root,
        InMemorySkillChangeStore store)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Skills:RootDirectory", root);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISkillChangeStore>(store);
                services.AddSingleton<ISkillChangeRecoveryStore>(store);
            });
        });
    }

    private static CapabilityEntry FindManageSkill(IServiceProvider services)
    {
        return services.GetRequiredService<ICapabilityInventory>()
            .Snapshot().Single(item => item.Name == "manage-skill");
    }

    private static Task<CapabilityExecutionResult> InvokeAsync(
        IServiceProvider services,
        Guid capabilityId,
        JsonElement arguments)
    {
        var request = new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capabilityId, arguments));
        return services.GetRequiredService<ICapabilityExecutor>()
            .ExecuteAsync(request, CancellationToken.None);
    }

    private static Task<string> ReadBodyAsync(
        IServiceProvider services,
        Guid skillId,
        CapabilityExecutionResult result)
    {
        return services.GetRequiredService<ISkillContentReader>().ReadBodyAsync(
            skillId, result.Evidence["replacement_version"], CancellationToken.None);
    }

    private static JsonElement AuthorArguments(Guid skillId)
    {
        return DocumentArguments("author", skillId, expectedVersion: null, "Start with geometry.");
    }

    private static JsonElement ReviseArguments(Guid skillId, string expectedVersion)
    {
        return DocumentArguments("revise", skillId, expectedVersion, "Finish with lighting.");
    }

    private static JsonElement DocumentArguments(
        string operation,
        Guid skillId,
        string? expectedVersion,
        string body)
    {
        return JsonSerializer.SerializeToElement(new
        {
            operation,
            skillId,
            expectedVersion,
            name = "live-comparison",
            description = "A live lifecycle demonstration.",
            body,
            tags = new[] { "test" },
            relatedCapabilities = Array.Empty<Guid>(),
            references = new Dictionary<string, string>(),
            diff = $"{operation} live skill",
        });
    }

    private static JsonElement RetireArguments(Guid skillId, string expectedVersion)
    {
        return JsonSerializer.SerializeToElement(new
        {
            operation = "retire",
            skillId,
            expectedVersion,
            diff = "retire live skill",
        });
    }

    private sealed class InMemorySkillChangeStore : ISkillChangeStore, ISkillChangeRecoveryStore
    {
        private readonly List<SkillChangeRecord> changes = [];
        private readonly HashSet<Guid> succeeded = [];

        public InMemorySkillChangeStore(params SkillChangeRecord[] changes)
        {
            this.changes.AddRange(changes);
        }

        public int SucceededCount => this.succeeded.Count;

        public Task<SkillChangeRecord> CreateAsync(
            SkillChangeRecord record,
            CancellationToken cancellationToken)
        {
            this.changes.Add(record);
            return Task.FromResult(record);
        }

        public Task<SkillChangeRecord?> FindAsync(
            Guid changeId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(this.changes.Find(
                item => item.Request.ChangeId == changeId));
        }

        public Task<bool> IsPendingAsync(Guid changeId, CancellationToken cancellationToken)
        {
            return Task.FromResult(!this.succeeded.Contains(changeId));
        }

        public Task<IReadOnlyList<SkillChangeRecord>> FindPendingAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<SkillChangeRecord> pending = this.changes
                .Where(item => !this.succeeded.Contains(item.Request.ChangeId))
                .Take(limit)
                .ToArray();
            return Task.FromResult(pending);
        }

        public Task RecordSucceededAsync(
            SkillChangeRecord record,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            this.succeeded.Add(record.Request.ChangeId);
            return Task.CompletedTask;
        }

        public Task RecordFailedAsync(
            SkillChangeRecord record,
            string failureCode,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
