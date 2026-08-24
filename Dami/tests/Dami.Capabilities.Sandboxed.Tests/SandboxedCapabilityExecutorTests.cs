using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxedCapabilityExecutorTests : IDisposable
{
    private readonly string scratch = Path.Combine(
        Path.GetTempPath(), "dami-sandboxed-executor-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.scratch))
        {
            Directory.Delete(this.scratch, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Changed_Assembly_Before_Process_Start_Async()
    {
        var capabilityId = Guid.NewGuid();
        string directory = Path.Combine(
            Path.GetTempPath(), "dami-sandboxed-executor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Tool.dll"), "changed");
            var verification = new ToolVerificationRecord(
                Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), new string('b', 64),
                "1 proposal test passed",
                new DateTimeOffset(2026, 8, 24, 23, 50, 0, TimeSpan.Zero));
            var registry = new SandboxedCapabilityRegistry();
            registry.Register(new SandboxedCapabilityRegistration(
                capabilityId, verification, directory));
            var runner = new RecordingRunner();
            var executor = new SandboxedCapabilityExecutor(registry, runner);
            CapabilityExecutionRequest request = CreateRequest(capabilityId);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => executor.ExecuteAsync(request, CancellationToken.None));

            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_Invoke_Exact_Registration_Read_Only_Async()
    {
        var capabilityId = Guid.NewGuid();
        var registration = this.CreateRegistration(capabilityId);
        var registry = new SandboxedCapabilityRegistry();
        registry.Register(registration);
        var runner = new RecordingRunner();
        var executor = new SandboxedCapabilityExecutor(registry, runner);
        CapabilityExecutionRequest request = CreateRequest(capabilityId);

        CapabilityExecutionResult result = await executor.ExecuteAsync(
            request, CancellationToken.None);

        Assert.True(executor.Owns(capabilityId));
        Assert.Equal("{\"result\":42}", result.Output);
        Assert.Equal("sandboxed", result.Evidence["source"]);
        Assert.Equal(registration.ArtifactVersion, result.Evidence["artifact_version"]);
        Assert.Equal(registration.ArtifactDirectory, runner.ToolDirectory);
        Assert.Equal(SandboxMountAccess.ReadOnly, runner.MountAccess);
        Assert.Equal(
            ["/usr/share/dotnet/dotnet", "/tool/Tool.dll"], runner.Command);
        Assert.Equal("{\"value\":42}", runner.StandardInput);
    }

    private SandboxedCapabilityRegistration CreateRegistration(Guid capabilityId)
    {
        Directory.CreateDirectory(this.scratch);
        File.WriteAllText(Path.Combine(this.scratch, "Tool.dll"), "assembly");
        string assemblySha256 = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("assembly")));
        var verification = new ToolVerificationRecord(
            Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), assemblySha256,
            "1 proposal test passed",
            new DateTimeOffset(2026, 8, 24, 23, 50, 0, TimeSpan.Zero));
        return new SandboxedCapabilityRegistration(
            capabilityId, verification, this.scratch);
    }

    private static CapabilityExecutionRequest CreateRequest(Guid capabilityId)
    {
        using var arguments = JsonDocument.Parse("""{"value":42}""");
        return new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly,
            ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capabilityId, arguments.RootElement));
    }

    private sealed class RecordingRunner : ISandboxProcessRunner
    {
        public int CallCount { get; private set; }

        public string? ToolDirectory { get; private set; }

        public SandboxMountAccess? MountAccess { get; private set; }

        public IReadOnlyList<string>? Command { get; private set; }

        public string? StandardInput { get; private set; }

        public Task<SandboxProcessResult> RunAsync(
            string toolDirectory,
            SandboxMountAccess mountAccess,
            IReadOnlyList<string> command,
            string standardInput,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.ToolDirectory = toolDirectory;
            this.MountAccess = mountAccess;
            this.Command = command;
            this.StandardInput = standardInput;
            return Task.FromResult(new SandboxProcessResult(0, "{\"result\":42}", string.Empty));
        }
    }
}
