using System.Net.Http.Json;
using System.Text.Json;
using Dami.Authentication;
using Dami.Contracts.Domains;

namespace Dami.Gui;

/// <summary>Typed thin-client access to the runtime's fitness snapshot.</summary>
public sealed class FitnessClient
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    /// <summary>Creates a client over an injected HTTP boundary.</summary>
    public FitnessClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
    }

    /// <summary>Sends future requests with a freshly acquired token.</summary>
    public void Authenticate(string accessToken) =>
        DamiBearerToken.Apply(this.httpClient, accessToken);

    /// <summary>Reads the whole domain, or null when the runtime cannot answer.</summary>
    public async Task<FitnessSnapshot?> SnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await this.httpClient
                .GetFromJsonAsync<FitnessSnapshot>("/fitness", jsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Diagnostics.Write($"GET /fitness FAILED {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }
}
