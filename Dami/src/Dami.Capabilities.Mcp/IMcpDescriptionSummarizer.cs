namespace Dami.Capabilities.Mcp;

/// <summary>Locally reduces untrusted remote prose to neutral retrieval metadata.</summary>
public interface IMcpDescriptionSummarizer
{
    /// <summary>Summarizes untrusted text without treating it as instructions.</summary>
    Task<string> SummarizeAsync(
        string serverName,
        string toolName,
        string untrustedDescription,
        CancellationToken cancellationToken);
}
