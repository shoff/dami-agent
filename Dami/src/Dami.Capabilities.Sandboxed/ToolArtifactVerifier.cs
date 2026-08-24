using System.Security.Cryptography;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Restores, builds, and tests a staged artifact only in the fixed envelope.</summary>
public sealed class ToolArtifactVerifier
{
    private static readonly string[] restoreCommand =
    [
        "/usr/share/dotnet/dotnet", "restore", "/tool/Tool.csproj", "--configfile",
        "/tool/NuGet.Config", "--nologo", "--disable-parallel",
    ];

    private static readonly string[] buildCommand =
    [
        "/usr/share/dotnet/dotnet", "build", "/tool/Tool.csproj", "--no-restore",
        "--configuration", "Release", "--output", "/tool/output", "--nologo",
        "--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false",
    ];

    private static readonly string[] testCommand =
    [
        "/usr/share/dotnet/dotnet", "/tool/output/Tool.dll", "--test",
    ];

    private readonly ToolEnvelopeWriter envelopeWriter;
    private readonly ISandboxProcessRunner processRunner;

    /// <summary>Creates the fixed verifier.</summary>
    public ToolArtifactVerifier(
        ToolEnvelopeWriter envelopeWriter,
        ISandboxProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(envelopeWriter);
        ArgumentNullException.ThrowIfNull(processRunner);
        this.envelopeWriter = envelopeWriter;
        this.processRunner = processRunner;
    }

    /// <summary>Verifies one exact artifact into a caller-owned scratch directory.</summary>
    public async Task<VerifiedToolArtifact> VerifyAsync(
        ToolProposalArtifact artifact,
        string scratchDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await this.envelopeWriter.WriteAsync(
            artifact, scratchDirectory, cancellationToken).ConfigureAwait(false);
        await this.RunRequiredAsync(
            scratchDirectory, SandboxMountAccess.WritableScratch,
            restoreCommand, "restore", cancellationToken).ConfigureAwait(false);
        await this.RunRequiredAsync(
            scratchDirectory, SandboxMountAccess.WritableScratch,
            buildCommand, "build", cancellationToken).ConfigureAwait(false);
        SandboxProcessResult tests = await this.RunRequiredAsync(
            scratchDirectory, SandboxMountAccess.ReadOnly,
            testCommand, "tests", cancellationToken).ConfigureAwait(false);
        string assemblyPath = Path.Combine(
            Path.GetFullPath(scratchDirectory), "output", "Tool.dll");
        if (!File.Exists(assemblyPath))
        {
            throw new InvalidDataException("The fixed build produced no tool assembly.");
        }

        string assemblySha256 = await HashAssemblyAsync(assemblyPath, cancellationToken)
            .ConfigureAwait(false);
        return new VerifiedToolArtifact(
            artifact.Version, assemblyPath, assemblySha256, tests.StandardOutput);
    }

    private async Task<SandboxProcessResult> RunRequiredAsync(
        string scratchDirectory,
        SandboxMountAccess access,
        IReadOnlyList<string> command,
        string phase,
        CancellationToken cancellationToken)
    {
        SandboxProcessResult result = await this.processRunner.RunAsync(
            scratchDirectory, access, command, string.Empty, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"Sandboxed tool {phase} failed with exit {result.ExitCode}: "
                + result.StandardOutput + result.StandardError);
        }

        return result;
    }

    private static async Task<string> HashAssemblyAsync(
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            assemblyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }
}
