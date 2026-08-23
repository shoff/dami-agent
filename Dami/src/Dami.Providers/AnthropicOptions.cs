namespace Dami.Providers;

/// <summary>The Anthropic adapter's configuration.</summary>
public sealed class AnthropicOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SECTION_NAME = "Anthropic";

    /// <summary>API endpoint. Its host must ALSO be on the egress allowlist — being the
    /// configured provider does not exempt it from the boundary (ADR-0010).</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>The model to complete with.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>API key. From user-secrets or the environment; never the repository.
    /// Empty means the adapter refuses everything with a clear message.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Cap on generated tokens per completion.</summary>
    public int MaxTokens { get; set; } = 2048;
}
