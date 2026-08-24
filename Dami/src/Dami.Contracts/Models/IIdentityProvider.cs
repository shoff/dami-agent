namespace Dami.Contracts.Models;

/// <summary>The stable identity block (§9.1) — one source, every provider (charter: models
/// are adapters, never the identity owner).</summary>
public interface IIdentityProvider
{
    /// <summary>The identity preamble for local turns. Stable across requests.</summary>
    string Preamble { get; }

    /// <summary>One compact voice line safe for frontier prompts — persona only, no
    /// personal data beyond the identity itself.</summary>
    string FrontierVoice { get; }
}
