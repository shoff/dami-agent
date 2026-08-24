using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxedToolMaterializerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "dami-tool-runtime-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    [Fact]
    public async Task MaterializeAsync_Should_Atomically_Install_Only_Verified_Runtime_Bytes_Async()
    {
        Directory.CreateDirectory(this.root);
        StagedToolProposal proposal = CreateProposal();
        ToolVerificationRecord verification = CreateVerification(proposal);
        string digest = verification.AssemblySha256;
        var runner = new BuildRunner();
        var verifier = new ToolArtifactVerifier(new ToolEnvelopeWriter(), runner);
        var materializer = new SandboxedToolMaterializer(this.root, verifier);
        var promotionId = Guid.NewGuid();

        var first = await materializer.MaterializeAsync(
            promotionId, proposal, verification, CancellationToken.None);
        var second = await materializer.MaterializeAsync(
            promotionId, proposal, verification, CancellationToken.None);

        await this.AssertInstalledAsync(proposal, verification, digest, first, second);
        Assert.Equal(3, runner.CallCount);
    }

    [Fact]
    public async Task MaterializeAsync_Should_Reject_A_Symbolic_Link_Capability_Directory_Async()
    {
        Directory.CreateDirectory(this.root);
        StagedToolProposal proposal = CreateProposal();
        string external = Path.Combine(
            Path.GetTempPath(), "dami-tool-external-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(external);
        Guid capabilityId = proposal.Request.Artifact.Schema.CapabilityId;
        Directory.CreateSymbolicLink(Path.Combine(this.root, capabilityId.ToString("D")), external);
        var runner = new BuildRunner();
        var materializer = new SandboxedToolMaterializer(
            this.root, new ToolArtifactVerifier(new ToolEnvelopeWriter(), runner));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => materializer.MaterializeAsync(
                Guid.NewGuid(), proposal, CreateVerification(proposal), CancellationToken.None));
            Assert.Equal(0, runner.CallCount);
            Assert.Empty(Directory.EnumerateFileSystemEntries(external));
        }
        finally
        {
            Directory.Delete(external, recursive: true);
        }
    }

    [Fact]
    public async Task MaterializeAsync_Should_Run_Installed_Bytes_In_The_Live_Sandbox_Async()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("DAMI_SANDBOX_INTEGRATION"),
            "1", StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(this.root);
        StagedToolProposal proposal = CreateConformingProposal();
        var runner = CreateRuntimeRunner();
        var verifier = new ToolArtifactVerifier(new ToolEnvelopeWriter(), runner);
        string bootstrap = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await this.AssertLiveMaterializationAsync(proposal, runner, verifier, bootstrap);
        }
        finally
        {
            if (Directory.Exists(bootstrap))
            {
                Directory.Delete(bootstrap, recursive: true);
            }
        }
    }

    private async Task AssertLiveMaterializationAsync(
        StagedToolProposal proposal,
        SandboxProcessRunner runner,
        ToolArtifactVerifier verifier,
        string bootstrap)
    {
        VerifiedToolArtifact artifact = await verifier.VerifyAsync(
            proposal.Request.Artifact, bootstrap, CancellationToken.None);
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            artifact.AssemblySha256, artifact.TestEvidence, DateTimeOffset.UnixEpoch);
        var materializer = new SandboxedToolMaterializer(this.root, verifier);
        SandboxedCapabilityRegistration registration = await materializer.MaterializeAsync(
            Guid.NewGuid(), proposal, verification, CancellationToken.None);

        SandboxProcessResult result = await runner.RunAsync(
            registration.ArtifactDirectory, SandboxMountAccess.ReadOnly,
            ["/usr/share/dotnet/dotnet", "/tool/Tool.dll"],
            "{\"value\":42}", CancellationToken.None);

        Assert.Equal((0, "{\"value\":42}"), (result.ExitCode, result.StandardOutput));
    }

    [Fact]
    public async Task MaterializeAsync_Should_Reject_Unexpected_Runtime_Directories_Async()
    {
        Directory.CreateDirectory(this.root);
        StagedToolProposal proposal = CreateProposal();
        ToolVerificationRecord verification = CreateVerification(proposal);
        var runner = new BuildRunner();
        var materializer = new SandboxedToolMaterializer(
            this.root, new ToolArtifactVerifier(new ToolEnvelopeWriter(), runner));
        SandboxedCapabilityRegistration installed = await materializer.MaterializeAsync(
            Guid.NewGuid(), proposal, verification, CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(installed.ArtifactDirectory, "unexpected"));

        await Assert.ThrowsAsync<InvalidDataException>(() => materializer.MaterializeAsync(
            Guid.NewGuid(), proposal, verification, CancellationToken.None));
    }

    private async Task AssertInstalledAsync(
        StagedToolProposal proposal,
        ToolVerificationRecord verification,
        string digest,
        SandboxedCapabilityRegistration first,
        SandboxedCapabilityRegistration second)
    {
        Assert.Equal(first.ArtifactDirectory, second.ArtifactDirectory);
        Assert.Equal(proposal.Request.Artifact.Schema.CapabilityId, first.CapabilityId);
        Assert.Equal(verification, first.Verification);
        Assert.Equal(
            ["Tool.dll", "Tool.runtimeconfig.json"],
            Directory.GetFiles(first.ArtifactDirectory)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            digest,
            Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(
                    Path.Combine(first.ArtifactDirectory, "Tool.dll")))));
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(this.root),
            path => Path.GetFileName(path).StartsWith(".dami-", StringComparison.Ordinal));
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "verified-tool", "Run verified behavior.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["verified"],
            new Dictionary<string, string> { ["VerifiedTool.cs"] = "source" },
            new Dictionary<string, string> { ["VerifiedToolTests.cs"] = "tests" },
            "Repeated observations justify the tool.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
    }

    private static StagedToolProposal CreateConformingProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "echo", "Echo input.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["echo"],
            new Dictionary<string, string> { ["EchoTool.cs"] = TOOL_SOURCE },
            new Dictionary<string, string> { ["EchoToolTests.cs"] = TEST_SOURCE },
            "The live materializer must execute.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, DateTimeOffset.UnixEpoch);
    }

    private static ToolVerificationRecord CreateVerification(StagedToolProposal proposal)
    {
        string digest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("verified-assembly")));
        return new ToolVerificationRecord(
            Guid.NewGuid(), proposal.Request.ProposalId, proposal.ArtifactVersion,
            digest, "tests_passed=1", DateTimeOffset.UnixEpoch);
    }

    private static SandboxProcessRunner CreateRuntimeRunner()
    {
        var options = new SandboxProcessOptions
        {
            MaxOutputBytes = 65_536,
            MemoryMaxBytes = 2_147_483_648,
            ProcessMax = 128,
            RuntimeMax = TimeSpan.FromSeconds(60),
            UserRuntimeDirectory = "/run/user/1000",
        };
        return new SandboxProcessRunner(new BubblewrapCommandFactory(options), options);
    }

    private sealed class BuildRunner : ISandboxProcessRunner
    {
        public int CallCount { get; private set; }

        public async Task<SandboxProcessResult> RunAsync(
            string toolDirectory,
            SandboxMountAccess mountAccess,
            IReadOnlyList<string> command,
            string standardInput,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            if (command.Contains("build", StringComparer.Ordinal))
            {
                string output = Path.Combine(toolDirectory, "output");
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(
                    Path.Combine(output, "Tool.dll"), "verified-assembly", cancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(output, "untrusted-extra.dll"), "not installed", cancellationToken);
            }

            return new SandboxProcessResult(0, "tests_passed=1", string.Empty);
        }
    }

    private const string TOOL_SOURCE = """
        using Dami.Sandbox;

        public sealed class EchoTool : ISandboxedTool
        {
            public ValueTask<string> ExecuteAsync(
                string inputJson,
                CancellationToken cancellationToken) => ValueTask.FromResult(inputJson);
        }
        """;

    private const string TEST_SOURCE = """
        using Dami.Sandbox;

        public sealed class EchoToolTests : ISandboxedToolTest
        {
            public async ValueTask RunAsync(CancellationToken cancellationToken)
            {
                var tool = new EchoTool();
                string result = await tool.ExecuteAsync("{\"value\":42}", cancellationToken);
                if (result != "{\"value\":42}")
                {
                    throw new InvalidOperationException("Echo result differed.");
                }
            }
        }
        """;
}
