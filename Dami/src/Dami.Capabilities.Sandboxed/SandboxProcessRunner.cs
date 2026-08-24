using System.Diagnostics;
using System.Text;
using Dami.Capabilities.Processes;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Runs and externally bounds one systemd-owned bubblewrap process tree.</summary>
public sealed class SandboxProcessRunner : ISandboxProcessRunner
{
    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    private readonly ISandboxCommandFactory commandFactory;
    private readonly int maxInputBytes;
    private readonly int maxOutputBytes;
    private readonly TimeSpan runtimeMax;
    private readonly string userRuntimeDirectory;

    /// <summary>Creates the bounded process runner.</summary>
    public SandboxProcessRunner(
        ISandboxCommandFactory commandFactory,
        SandboxProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(options);
        SandboxProcessOptionsGuard.Validate(options);

        this.commandFactory = commandFactory;
        this.maxInputBytes = options.MaxInputBytes;
        this.maxOutputBytes = options.MaxOutputBytes;
        this.runtimeMax = options.RuntimeMax;
        this.userRuntimeDirectory = options.UserRuntimeDirectory;
    }

    /// <summary>Runs a trusted command with one bounded stdin value.</summary>
    public async Task<SandboxProcessResult> RunAsync(
        string toolDirectory,
        SandboxMountAccess mountAccess,
        IReadOnlyList<string> command,
        string standardInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(standardInput);
        this.ValidateInput(standardInput);
        string unitName = "dami-tool-" + Guid.NewGuid().ToString("N");
        using Process process = this.CreateProcess(
            toolDirectory, mountAccess, command, unitName);
        if (!process.Start())
        {
            throw new InvalidOperationException("The sandbox process did not start.");
        }

        try
        {
            return await this.RunStartedAsync(
                process, unitName, standardInput, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryKill(process);
        }
    }

    private void ValidateInput(string standardInput)
    {
        try
        {
            if (strictUtf8.GetByteCount(standardInput) > this.maxInputBytes)
            {
                throw new InvalidDataException(
                    $"Sandbox input exceeds {this.maxInputBytes} bytes.");
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("Sandbox input is not valid UTF-8.", exception);
        }
    }

    private Process CreateProcess(
        string toolDirectory,
        SandboxMountAccess mountAccess,
        IReadOnlyList<string> command,
        string unitName)
    {
        ProcessStartInfo start = this.commandFactory.Create(
            toolDirectory, mountAccess, command, unitName);
        return new Process { StartInfo = start };
    }

    private async Task<SandboxProcessResult> RunStartedAsync(
        Process process,
        string unitName,
        string standardInput,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(this.runtimeMax);
        using var stdout = new BoundedProcessOutput(this.maxOutputBytes);
        using var stderr = new BoundedProcessOutput(this.maxOutputBytes);
        var budget = new SharedOutputBudget(this.maxOutputBytes);
        using var registration = stop.Token.Register(() => TryKill(process));
        try
        {
            await Task.WhenAll(
                WriteInputAsync(process, standardInput, stop.Token),
                stdout.CaptureAsync(process.StandardOutput.BaseStream, budget, stop),
                stderr.CaptureAsync(process.StandardError.BaseStream, budget, stop),
                process.WaitForExitAsync(stop.Token)).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldContain(exception))
        {
            await this.StopUnitAsync(unitName).ConfigureAwait(false);
            this.ThrowContained(exception, budget, cancellationToken);
        }

        return new SandboxProcessResult(
            process.ExitCode, stdout.Decode(strictUtf8), stderr.Decode(strictUtf8));
    }

    private static async Task WriteInputAsync(
        Process process,
        string value,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteAsync(value.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private static bool ShouldContain(Exception exception)
    {
        return exception is OperationCanceledException or OutputLimitExceededException;
    }

    private void ThrowContained(
        Exception exception,
        SharedOutputBudget budget,
        CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested)
        {
            callerCancellation.ThrowIfCancellationRequested();
        }

        if (budget.Exceeded)
        {
            throw new InvalidDataException(
                $"Sandbox output exceeds {this.maxOutputBytes} bytes.", exception);
        }

        throw new TimeoutException(
            $"Sandbox execution exceeded {this.runtimeMax}.", exception);
    }

    private async Task StopUnitAsync(string unitName)
    {
        using var stop = new Process { StartInfo = this.CreateStopInfo(unitName) };
        if (!stop.Start())
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await stop.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(stop);
        }
    }

    private ProcessStartInfo CreateStopInfo(string unitName)
    {
        var start = new ProcessStartInfo
        {
            FileName = "/usr/bin/systemctl",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment.Clear();
        start.Environment["XDG_RUNTIME_DIR"] = this.userRuntimeDirectory;
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] =
            $"unix:path={this.userRuntimeDirectory}/bus";
        start.ArgumentList.Add("--user");
        start.ArgumentList.Add("stop");
        start.ArgumentList.Add(unitName);
        return start;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }
}
