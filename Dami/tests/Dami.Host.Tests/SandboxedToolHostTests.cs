using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dami.Capabilities;
using Dami.Capabilities.Sandboxed;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dami.Host.Tests;

public sealed class SandboxedToolHostTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "dami-host-tools-" + Guid.NewGuid().ToString("N")))
        .FullName;

    public void Dispose() => Directory.Delete(this.root, recursive: true);

    [Fact]
    public async Task Host_Should_Compose_Sandboxed_Execution_When_Configured_Async()
    {
        var source = Substitute.For<IToolActivationRecoverySource>();
        source.FindAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        await using WebApplicationFactory<Program> factory = this.CreateFactory(source);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/health", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains(
            factory.Services.GetServices<ICapabilityExecutionSource>(),
            item => item is SandboxedCapabilityExecutor);
        Assert.IsType<SandboxedCapabilityRegistry>(
            factory.Services.GetRequiredService<ISandboxedCapabilityCatalog>());
    }

    [Fact]
    public async Task Host_Should_Refuse_A_Missing_Sandbox_Runtime_Root_Async()
    {
        string missing = Path.Combine(this.root, "missing");
        var source = Substitute.For<IToolActivationRecoverySource>();
        source.FindAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        await using WebApplicationFactory<Program> factory = this.CreateFactory(source, missing);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using HttpClient client = factory.CreateClient();
            await client.GetAsync("/health", CancellationToken.None);
        });
    }

    [Fact]
    public async Task Host_Restart_Should_Republish_An_Activated_Exact_Tool_Async()
    {
        ToolActivationRecoveryItem item = await this.InstallActivatedToolAsync();
        var source = Substitute.For<IToolActivationRecoverySource>();
        source.FindAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([item]);
        var runner = Substitute.For<ISandboxProcessRunner>();
        runner.RunAsync(
                Arg.Any<string>(), Arg.Any<SandboxMountAccess>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxProcessResult(0, "recovered output", string.Empty));

        CapabilityEntry first = await this.RecoverAndInvokeAsync(item, source, runner);
        CapabilityEntry second = await this.RecoverAndInvokeAsync(item, source, runner);

        Assert.NotSame(first, second);
        Assert.Equal(item.Proposal.ArtifactVersion, second.Version);
        await source.Received(2).FindAsync(1_000, Arg.Any<CancellationToken>());
    }

    private WebApplicationFactory<Program> CreateFactory(
        IToolActivationRecoverySource source,
        string? root = null,
        ISandboxProcessRunner? runner = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SandboxedTools:RootDirectory", root ?? this.root);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(source);
                if (runner is not null)
                {
                    services.AddSingleton(runner);
                }
            });
        });
    }

    private async Task<CapabilityEntry> RecoverAndInvokeAsync(
        ToolActivationRecoveryItem item,
        IToolActivationRecoverySource source,
        ISandboxProcessRunner runner)
    {
        await using WebApplicationFactory<Program> factory = this.CreateFactory(source, runner: runner);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage health = await client.GetAsync("/health", CancellationToken.None);
        CapabilityEntry entry = factory.Services.GetRequiredService<ICapabilityCatalog>()
            .Find(item.Proposal.Request.Artifact.Schema.CapabilityId)!;
        CapabilityExecutionResult result = await InvokeAsync(factory.Services, entry.CapabilityId);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("recovered output", result.Output);
        return entry;
    }

    private async Task<ToolActivationRecoveryItem> InstallActivatedToolAsync()
    {
        StagedToolProposal proposal = CreateProposal();
        byte[] assembly = Encoding.UTF8.GetBytes("exact installed assembly");
        string digest = Convert.ToHexStringLower(SHA256.HashData(assembly));
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            digest, "tests_passed=1", DateTimeOffset.UnixEpoch);
        string directory = Path.Combine(
            this.root, proposal.Request.Artifact.Schema.CapabilityId.ToString("D"),
            proposal.ArtifactVersion);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "Tool.dll"), assembly);
        await File.WriteAllTextAsync(Path.Combine(directory, "Tool.runtimeconfig.json"), RUNTIME_CONFIG);
        return new ToolActivationRecoveryItem(Guid.NewGuid(), proposal, verification, true);
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "recovered-tool", "Recovered after restart.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["recovery"],
            new Dictionary<string, string> { ["Tool.cs"] = "source" },
            new Dictionary<string, string> { ["ToolTests.cs"] = "tests" },
            "Restart must republish approved tools.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
    }

    private static Task<CapabilityExecutionResult> InvokeAsync(
        IServiceProvider services,
        Guid capabilityId)
    {
        var request = new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capabilityId, JsonSerializer.SerializeToElement(new { value = 1 })));
        return services.GetRequiredService<ICapabilityExecutor>()
            .ExecuteAsync(request, CancellationToken.None);
    }

    private const string RUNTIME_CONFIG =
        "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}";
}
