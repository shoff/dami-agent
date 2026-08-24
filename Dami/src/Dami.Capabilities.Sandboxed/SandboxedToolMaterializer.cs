using System.Text;
using Dami.Contracts.ToolStaging;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Converges one verified proposal into a version-addressed runtime directory.</summary>
public sealed class SandboxedToolMaterializer : ISandboxedToolMaterializer
{
    private const string RUNTIME_CONFIGURATION = """
        {"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}
        """;

    private static readonly UTF8Encoding utf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string rootDirectory;
    private readonly IToolArtifactVerifier verifier;

    /// <summary>Creates an immutable runtime materializer rooted at one private directory.</summary>
    public SandboxedToolMaterializer(
        string rootDirectory,
        IToolArtifactVerifier verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(verifier);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.verifier = verifier;
    }

    /// <summary>Builds, verifies, and atomically installs one exact approved artifact.</summary>
    public async Task<SandboxedCapabilityRegistration> MaterializeAsync(
        Guid promotionId,
        StagedToolProposal proposal,
        ToolVerificationRecord verification,
        CancellationToken cancellationToken)
    {
        Validate(promotionId, proposal, verification);
        EnsureOrdinaryDirectory(this.rootDirectory);
        Guid capabilityId = proposal.Request.Artifact.Schema.CapabilityId;
        string parent = Path.Combine(this.rootDirectory, capabilityId.ToString("D"));
        Directory.CreateDirectory(parent);
        EnsureOrdinaryDirectory(parent);
        string target = Path.Combine(parent, proposal.ArtifactVersion);
        if (Directory.Exists(target))
        {
            return await InspectAsync(
                capabilityId, target, verification, cancellationToken).ConfigureAwait(false);
        }

        return await this.MaterializeNewAsync(
            promotionId, proposal, verification, capabilityId, target, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SandboxedCapabilityRegistration> MaterializeNewAsync(
        Guid promotionId,
        StagedToolProposal proposal,
        ToolVerificationRecord verification,
        Guid capabilityId,
        string target,
        CancellationToken cancellationToken)
    {
        string verify = Path.Combine(this.rootDirectory, $".dami-verify-{promotionId:N}");
        string stage = Path.Combine(this.rootDirectory, $".dami-install-{promotionId:N}");
        try
        {
            DeleteDirectory(verify);
            DeleteDirectory(stage);
            VerifiedToolArtifact artifact = await this.verifier.VerifyAsync(
                proposal.Request.Artifact, verify, cancellationToken).ConfigureAwait(false);
            EnsureVerified(artifact, verification);
            Directory.CreateDirectory(stage);
            await CopyAssemblyAsync(artifact.AssemblyPath, stage, cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(stage, "Tool.runtimeconfig.json"),
                RUNTIME_CONFIGURATION, utf8, cancellationToken).ConfigureAwait(false);
            await InspectAsync(capabilityId, stage, verification, cancellationToken)
                .ConfigureAwait(false);
            MoveOrAcceptExact(stage, target);
            return await InspectAsync(
                capabilityId, target, verification, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteDirectory(verify);
            DeleteDirectory(stage);
        }
    }

    private static async Task CopyAssemblyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            Path.Combine(destination, "Tool.dll"), FileMode.CreateNew, FileAccess.Write,
            FileShare.None, bufferSize: 65_536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void EnsureOrdinaryDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Sandboxed tool root '{path}' does not exist.");
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Symbolic-link sandboxed tool roots are not allowed.");
        }
    }

    private static void EnsureVerified(
        VerifiedToolArtifact artifact,
        ToolVerificationRecord verification)
    {
        if (!string.Equals(
                artifact.ArtifactVersion, verification.ArtifactVersion, StringComparison.Ordinal)
            || !string.Equals(
                artifact.AssemblySha256, verification.AssemblySha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Rebuilt sandboxed tool bytes do not match the durable verification.");
        }
    }

    private static async Task<SandboxedCapabilityRegistration> InspectAsync(
        Guid capabilityId,
        string directory,
        ToolVerificationRecord verification,
        CancellationToken cancellationToken)
    {
        EnsureOrdinaryDirectory(directory);
        string[] entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
        string[] names = entries
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        bool entriesAreOrdinaryFiles = entries.All(path =>
            File.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
        if (!entriesAreOrdinaryFiles
            || !names.SequenceEqual(
                ["Tool.dll", "Tool.runtimeconfig.json"], StringComparer.Ordinal))
        {
            throw new InvalidDataException("A materialized sandboxed tool has unexpected files.");
        }

        string observed = await ToolAssemblyDigest.ComputeAsync(
            Path.Combine(directory, "Tool.dll"), cancellationToken).ConfigureAwait(false);
        string runtimeConfiguration = await File.ReadAllTextAsync(
            Path.Combine(directory, "Tool.runtimeconfig.json"), cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(observed, verification.AssemblySha256, StringComparison.Ordinal)
            || !string.Equals(
                runtimeConfiguration, RUNTIME_CONFIGURATION, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A materialized sandboxed tool differs from its verified runtime bytes.");
        }

        return new SandboxedCapabilityRegistration(capabilityId, verification, directory);
    }

    private static void MoveOrAcceptExact(string stage, string target)
    {
        try
        {
            Directory.Move(stage, target);
        }
        catch (IOException) when (Directory.Exists(target))
        {
            DeleteDirectory(stage);
        }
    }

    private static void Validate(
        Guid promotionId,
        StagedToolProposal proposal,
        ToolVerificationRecord verification)
    {
        if (promotionId == Guid.Empty)
        {
            throw new ArgumentException("A promotion identifier cannot be empty.", nameof(promotionId));
        }

        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.ProposalId != proposal.Request.ProposalId
            || !string.Equals(
                verification.ArtifactVersion, proposal.ArtifactVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Materialization requires verification of the exact staged proposal.",
                nameof(verification));
        }
    }
}
