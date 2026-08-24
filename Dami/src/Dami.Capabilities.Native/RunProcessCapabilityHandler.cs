using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Runs one allowlisted executable without invoking a shell.</summary>
[NativeCapability(
    "4e448f5c-66e2-46ea-8e75-d73b8853d492",
    "run-process",
    "Run an allowlisted executable with literal arguments beneath the workspace root.",
    "native://run-process/schema/v1",
    "1.0.0",
    Tags = new[] { "terminal", "process" })]
public sealed class RunProcessCapabilityHandler : INativeCapabilityHandler
{
    private const int ABSOLUTE_MAX_OUTPUT_BYTES = 4 * 1024 * 1024;

    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    private readonly IReadOnlyDictionary<string, string> allowedExecutables;
    private readonly int maxOutputBytes;
    private readonly string rootDirectory;

    /// <summary>Creates the no-shell process handler.</summary>
    public RunProcessCapabilityHandler(RunProcessCapabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootDirectory);
        ArgumentNullException.ThrowIfNull(options.AllowedExecutables);
        if (options.MaxOutputBytes is <= 0 or > ABSOLUTE_MAX_OUTPUT_BYTES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxOutputBytes,
                $"MaxOutputBytes must be between 1 and {ABSOLUTE_MAX_OUTPUT_BYTES}.");
        }

        var root = new DirectoryInfo(Path.GetFullPath(options.RootDirectory));
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException(root.FullName);
        }

        this.rootDirectory = root.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root.FullName;
        this.allowedExecutables = SnapshotAllowlist(options.AllowedExecutables);
        this.maxOutputBytes = options.MaxOutputBytes;
    }

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var processRequest = Parse(request.Invocation.Arguments);
        if (!this.allowedExecutables.TryGetValue(processRequest.Executable, out var executablePath))
        {
            throw new UnauthorizedAccessException(
                $"Executable alias '{processRequest.Executable}' is not allowlisted.");
        }

        using var process = CreateProcess(executablePath, this.rootDirectory, processRequest.Arguments);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Executable '{processRequest.Executable}' did not start.");
        }

        try
        {
            return await this.RunStartedAsync(
                process, processRequest.Executable, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryKill(process);
        }
    }

    private static Process CreateProcess(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            CreateNoWindow = true,
            FileName = executablePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }

    private async Task<CapabilityExecutionResult> RunStartedAsync(
        Process process,
        string executableAlias,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var stdout = new BoundedProcessOutput(this.maxOutputBytes);
        using var stderr = new BoundedProcessOutput(this.maxOutputBytes);
        var budget = new SharedOutputBudget(this.maxOutputBytes);
        using var registration = stop.Token.Register(() => TryKill(process));
        try
        {
            await Task.WhenAll(
                stdout.CaptureAsync(process.StandardOutput.BaseStream, budget, stop),
                stderr.CaptureAsync(process.StandardError.BaseStream, budget, stop),
                process.WaitForExitAsync(stop.Token)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsOutputLimit(exception, budget, cancellationToken))
        {
            throw this.CreateOutputLimitException();
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Executable '{executableAlias}' exited {process.ExitCode}: {stderr.Decode(strictUtf8)}");
        }

        return CreateResult(process.ExitCode, executableAlias, stdout.Decode(strictUtf8));
    }

    private static CapabilityExecutionResult CreateResult(
        int exitCode,
        string executableAlias,
        string output)
    {
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["executable"] = executableAlias,
            ["exit_code"] = exitCode.ToString(CultureInfo.InvariantCulture),
            ["stdout_bytes"] = Encoding.UTF8.GetByteCount(output).ToString(CultureInfo.InvariantCulture),
        };
        return new CapabilityExecutionResult(output, evidence);
    }

    private static bool IsOutputLimit(
        Exception exception,
        SharedOutputBudget budget,
        CancellationToken callerCancellation)
    {
        return !callerCancellation.IsCancellationRequested
            && budget.Exceeded
            && exception is OutputLimitExceededException or OperationCanceledException;
    }

    private InvalidDataException CreateOutputLimitException()
    {
        return new InvalidDataException(
            $"Process output exceeds the configured combined limit of {this.maxOutputBytes} bytes.");
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

    private static IReadOnlyDictionary<string, string> SnapshotAllowlist(
        IReadOnlyDictionary<string, string> allowedExecutables)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (alias, path) in allowedExecutables)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            {
                throw new ArgumentException(
                    $"Allowlisted executable '{alias}' must map to an existing absolute file.",
                    nameof(allowedExecutables));
            }

            snapshot.Add(alias, Path.GetFullPath(path));
        }

        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    private static ProcessRequest Parse(JsonElement arguments)
    {
        var executable = ReadRequiredString(arguments, "executable");
        if (!arguments.TryGetProperty("arguments", out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Run-process arguments require an 'arguments' array.", nameof(arguments));
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } value)
            {
                throw new ArgumentException("Every process argument must be a string.", nameof(arguments));
            }

            values.Add(value);
        }

        return new ProcessRequest(executable, values);
    }

    private static string ReadRequiredString(JsonElement arguments, string propertyName)
    {
        if (arguments.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new ArgumentException(
            $"Run-process arguments require a non-empty string '{propertyName}'.",
            nameof(arguments));
    }

    private sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments);
}
