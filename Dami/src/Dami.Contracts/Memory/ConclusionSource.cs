namespace Dami.Contracts.Memory;

/// <summary>How a conclusion came to be believed.</summary>
/// <remarks>
/// Not enumerated in the architecture document; proposed here. Provenance is what makes
/// the ledger auditable — "why does Dami think this" must have an answer that is not
/// "the model said so".
/// </remarks>
public enum ConclusionSource
{
    /// <summary>Inferred from something said in conversation.</summary>
    Conversation = 0,

    /// <summary>Produced by the weekly cross-domain reflection pass.</summary>
    ReflectionPass = 1,

    /// <summary>Stated directly by Steve, and therefore not an inference at all.</summary>
    DirectStatement = 2,

    /// <summary>Produced by Dami examining its own behaviour (D-011).</summary>
    SelfAudit = 3,

    /// <summary>A correction that supersedes an earlier conclusion.</summary>
    Correction = 4,
}
