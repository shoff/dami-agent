namespace Dami.Providers;

/// <summary>Which door image generation goes through (ADR-0035).</summary>
public enum ImageProviderKind
{
    /// <summary>The Codex subscription's built-in image tool (ADR-0029). No key, no bill.</summary>
    Codex = 0,

    /// <summary>The keyed OpenAI images API (ADR-0027).</summary>
    OpenAi = 1,

    /// <summary>The keyed Gemini API (ADR-0035).</summary>
    Gemini = 2,
}
