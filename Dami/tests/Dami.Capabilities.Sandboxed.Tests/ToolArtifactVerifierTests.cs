using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class ToolArtifactVerifierTests : IDisposable
{
    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-tool-verifier-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.scratch))
        {
            Directory.Delete(this.scratch, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_Should_Restore_Build_And_Test_Only_The_Fixed_Envelope()
    {
        ToolProposalArtifact artifact = CreateArtifact();
        var runner = new RecordingRunner(this.scratch);
        var verifier = new ToolArtifactVerifier(new ToolEnvelopeWriter(), runner);

        VerifiedToolArtifact verified = await verifier.VerifyAsync(
            artifact, this.scratch, CancellationToken.None);

        Assert.Equal(artifact.Version, verified.ArtifactVersion);
        Assert.Equal(Path.Combine(this.scratch, "output", "Tool.dll"), verified.AssemblyPath);
        Assert.Equal(
            [SandboxMountAccess.WritableScratch, SandboxMountAccess.WritableScratch,
                SandboxMountAccess.ReadOnly],
            runner.Calls.Select(call => call.Access));
        Assert.Equal(["restore", "build", "/tool/output/Tool.dll"],
            runner.Calls.Select(call => call.Command[1]));
    }

    [Fact]
    public async Task VerifyAsync_Should_Build_And_Run_Conforming_Tests_In_The_Live_Sandbox()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("DAMI_SANDBOX_INTEGRATION"),
            "1", StringComparison.Ordinal))
        {
            return;
        }

        var options = new SandboxProcessOptions
        {
            MaxOutputBytes = 1_048_576,
            MemoryMaxBytes = 2_147_483_648,
            ProcessMax = 128,
            RuntimeMax = TimeSpan.FromSeconds(60),
            UserRuntimeDirectory = "/run/user/1000",
        };
        var verificationRunner = new SandboxProcessRunner(
            new BubblewrapCommandFactory(options), options);
        var verifier = new ToolArtifactVerifier(new ToolEnvelopeWriter(), verificationRunner);

        VerifiedToolArtifact verified = await verifier.VerifyAsync(
            CreateConformingArtifact(), this.scratch, CancellationToken.None);
        SandboxProcessResult invocation = await CreateRuntimeRunner().RunAsync(
            this.scratch, SandboxMountAccess.ReadOnly,
            ["/usr/share/dotnet/dotnet", "/tool/output/Tool.dll"],
            "{\"value\":42}", CancellationToken.None);

        Assert.True(File.Exists(verified.AssemblyPath));
        Assert.Equal("tests_passed=1", verified.TestEvidence);
        Assert.Equal((0, "{\"value\":42}"), (invocation.ExitCode, invocation.StandardOutput));
        Assert.False(File.Exists(Path.Combine(this.scratch, "escape-marker")));
    }

    private static ToolProposalArtifact CreateArtifact()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "echo", "Echo input.", parameters.RootElement);
        return new ToolProposalArtifact(
            schema, ["echo"],
            new Dictionary<string, string> { ["EchoTool.cs"] = "source" },
            new Dictionary<string, string> { ["EchoToolTests.cs"] = "tests" },
            "No existing tool performs this pure transform.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
    }

    private static ToolProposalArtifact CreateConformingArtifact()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "echo", "Echo input.", parameters.RootElement);
        return new ToolProposalArtifact(
            schema, ["echo"],
            new Dictionary<string, string> { ["EchoTool.cs"] = TOOL_SOURCE },
            new Dictionary<string, string> { ["EchoToolTests.cs"] = TEST_SOURCE },
            "No existing tool performs this pure transform.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
    }

    private static SandboxProcessRunner CreateRuntimeRunner()
    {
        var options = new SandboxProcessOptions
        {
            MaxOutputBytes = 65_536,
            MemoryMaxBytes = 268_435_456,
            ProcessMax = 16,
            RuntimeMax = TimeSpan.FromSeconds(15),
            UserRuntimeDirectory = "/run/user/1000",
        };
        return new SandboxProcessRunner(new BubblewrapCommandFactory(options), options);
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
        using System.Net;
        using System.Net.Sockets;
        using Dami.Sandbox;

        public sealed class EchoToolTests : ISandboxedToolTest
        {
            public async ValueTask RunAsync(CancellationToken cancellationToken)
            {
                if (Directory.Exists("/home/steve") || File.Exists("/etc/shadow"))
                {
                    throw new InvalidOperationException("Host-private paths are visible.");
                }

                try
                {
                    await File.WriteAllTextAsync("/tool/escape-marker", "escape", cancellationToken);
                    throw new InvalidOperationException("The tool mount was writable.");
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }

                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream,
                    ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Parse("1.1.1.1"), 53, cancellationToken);
                    throw new InvalidOperationException("The network was reachable.");
                }
                catch (SocketException)
                {
                }

                var tool = new EchoTool();
                string result = await tool.ExecuteAsync("{\"value\":42}", cancellationToken);
                if (result != "{\"value\":42}")
                {
                    throw new InvalidOperationException("Echo result differed.");
                }
            }
        }
        """;

    private sealed class RecordingRunner(string root) : ISandboxProcessRunner
    {
        public List<Call> Calls { get; } = [];

        public async Task<SandboxProcessResult> RunAsync(
            string toolDirectory,
            SandboxMountAccess mountAccess,
            IReadOnlyList<string> command,
            string standardInput,
            CancellationToken cancellationToken)
        {
            this.Calls.Add(new Call(mountAccess, command));
            if (command.Contains("build", StringComparer.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(root, "output"));
                await File.WriteAllTextAsync(
                    Path.Combine(root, "output", "Tool.dll"), "assembly", cancellationToken);
            }

            return new SandboxProcessResult(0, "tests_passed=1", "");
        }
    }

    private sealed record Call(SandboxMountAccess Access, IReadOnlyList<string> Command);
}
