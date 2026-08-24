using System.Diagnostics;

namespace Dami.Capabilities.Sandboxed.Tests;

public sealed class SandboxProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_Stream_Input_And_Return_Bounded_Utf8_Output()
    {
        var options = new SandboxProcessOptions
        {
            MaxOutputBytes = 128,
            RuntimeMax = TimeSpan.FromSeconds(5),
        };
        var runner = new SandboxProcessRunner(new TeeCommandFactory(), options);

        SandboxProcessResult result = await runner.RunAsync(
            "/tmp", SandboxMountAccess.ReadOnly, ["/ignored"], "hello",
            CancellationToken.None);

        Assert.Equal((0, "hello", ""), (result.ExitCode, result.StandardOutput,
            result.StandardError));
    }

    [Fact]
    public async Task RunAsync_Should_Reject_Input_Above_The_Bound_Before_Process_Start()
    {
        var factory = new RecordingCommandFactory();
        var options = new SandboxProcessOptions
        {
            MaxInputBytes = 4,
            RuntimeMax = TimeSpan.FromSeconds(5),
        };
        var runner = new SandboxProcessRunner(factory, options);

        await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunAsync(
            "/tmp", SandboxMountAccess.ReadOnly, ["/ignored"], "12345",
            CancellationToken.None));

        Assert.False(factory.Created);
    }

    [Fact]
    public async Task RunAsync_Should_Kill_A_Process_That_Exceeds_The_Output_Bound()
    {
        var options = new SandboxProcessOptions
        {
            MaxOutputBytes = 64,
            RuntimeMax = TimeSpan.FromSeconds(5),
        };
        var runner = new SandboxProcessRunner(new FixedCommandFactory("/usr/bin/yes"), options);

        await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunAsync(
            "/tmp", SandboxMountAccess.ReadOnly, ["/ignored"], string.Empty,
            CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Should_Kill_A_Process_That_Exceeds_The_Time_Bound()
    {
        var options = new SandboxProcessOptions { RuntimeMax = TimeSpan.FromSeconds(1) };
        var runner = new SandboxProcessRunner(
            new FixedCommandFactory("/usr/bin/sleep", "30"), options);

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            "/tmp", SandboxMountAccess.ReadOnly, ["/ignored"], string.Empty,
            CancellationToken.None));
    }

    private sealed class TeeCommandFactory : ISandboxCommandFactory
    {
        public ProcessStartInfo Create(
            string toolDirectory,
            SandboxMountAccess mountAccess,
            IReadOnlyList<string> command,
            string unitName)
        {
            return new ProcessStartInfo
            {
                FileName = "/usr/bin/tee",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
        }
    }

    private sealed class RecordingCommandFactory : ISandboxCommandFactory
    {
        public bool Created { get; private set; }

        public ProcessStartInfo Create(
            string toolDirectory,
            SandboxMountAccess mountAccess,
            IReadOnlyList<string> command,
            string unitName)
        {
            this.Created = true;
            return new ProcessStartInfo();
        }
    }

    private sealed class FixedCommandFactory(string executable, params string[] arguments)
        : ISandboxCommandFactory
    {
        public ProcessStartInfo Create(
            string toolDirectory,
            SandboxMountAccess mountAccess,
            IReadOnlyList<string> command,
            string unitName)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }
    }
}
