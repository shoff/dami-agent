namespace Dami.Contracts.Privacy;

/// <summary>What came back from an egress request.</summary>
public sealed record EgressResponse
{
    /// <summary>Creates a response.</summary>
    public EgressResponse(int statusCode, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        this.StatusCode = statusCode;
        this.Body = body;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The response body as text.</summary>
    public string Body { get; }
}
