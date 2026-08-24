namespace Dami.Capabilities;

/// <summary>Bounds for semantic capability retrieval.</summary>
public sealed class CapabilityRetrievalOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SECTION_NAME = "CapabilityRetrieval";

    /// <summary>Gets or sets the ANN candidate count sent to the reranker.</summary>
    public int CandidateLimit { get; set; } = 50;

    /// <summary>Gets or sets the reranked capability count expanded into a bundle.</summary>
    public int ResultLimit { get; set; } = 8;
}
