using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Executes only registered verified assemblies through the OS sandbox.</summary>
public sealed class SandboxedCapabilityExecutor : ICapabilityExecutionSource
{
    private static readonly string[] command =
    [
        "/usr/share/dotnet/dotnet", "/tool/Tool.dll",
    ];

    private readonly ISandboxedCapabilityCatalog catalog;
    private readonly ISandboxProcessRunner processRunner;

    /// <summary>Creates the sandboxed execution source.</summary>
    public SandboxedCapabilityExecutor(
        ISandboxedCapabilityCatalog catalog,
        ISandboxProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(processRunner);
        this.catalog = catalog;
        this.processRunner = processRunner;
    }

    /// <inheritdoc />
    public bool Owns(Guid capabilityId) => this.catalog.Find(capabilityId) is not null;

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CapabilityInvocation invocation = request.Invocation;
        SandboxedCapabilityRegistration registration = this.catalog.Find(invocation.CapabilityId)
            ?? throw new KeyNotFoundException(
                $"Sandboxed capability '{invocation.CapabilityId}' is not registered.");
        await EnsureExactAssemblyAsync(registration, cancellationToken).ConfigureAwait(false);
        SandboxProcessResult result = await this.processRunner.RunAsync(
            registration.ArtifactDirectory,
            SandboxMountAccess.ReadOnly,
            command,
            invocation.Arguments.GetRawText(),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Sandboxed capability '{invocation.CapabilityId}' failed with exit "
                + $"{result.ExitCode}: {result.StandardOutput}{result.StandardError}");
        }

        return new CapabilityExecutionResult(
            result.StandardOutput,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "sandboxed",
                ["capability_id"] = invocation.CapabilityId.ToString("D"),
                ["artifact_version"] = registration.ArtifactVersion,
                ["assembly_sha256"] = registration.AssemblySha256,
            });
    }

    private static async Task EnsureExactAssemblyAsync(
        SandboxedCapabilityRegistration registration,
        CancellationToken cancellationToken)
    {
        string assemblyPath = Path.Combine(registration.ArtifactDirectory, "Tool.dll");
        string observed = await ToolAssemblyDigest.ComputeAsync(assemblyPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(observed, registration.AssemblySha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Sandboxed capability '{registration.CapabilityId}' assembly digest changed.");
        }
    }
}
