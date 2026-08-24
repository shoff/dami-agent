namespace Dami.Capabilities.Sandboxed;

/// <summary>Exact derived output that passed the fixed sandboxed verification.</summary>
public sealed record VerifiedToolArtifact(
    string ArtifactVersion,
    string AssemblyPath,
    string AssemblySha256,
    string TestEvidence);
