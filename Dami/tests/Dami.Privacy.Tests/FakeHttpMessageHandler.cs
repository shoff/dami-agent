using System.Net;

namespace Dami.Privacy.Tests;

/// <summary>A handler that answers every request with a canned response.</summary>
/// <remarks>
/// Hand-rolled because <c>HttpMessageHandler.SendAsync</c> is protected, which NSubstitute
/// cannot intercept cleanly. Records what was sent so tests can assert nothing was.
/// </remarks>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode statusCode;
    private readonly string body;

    /// <summary>Creates the handler.</summary>
    public FakeHttpMessageHandler(HttpStatusCode statusCode, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        this.statusCode = statusCode;
        this.body = body;
    }

    /// <summary>Every request that reached the network layer.</summary>
    public List<Uri> Sent { get; } = [];

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        this.Sent.Add(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(this.statusCode)
        {
            Content = new StringContent(this.body),
        });
    }
}
