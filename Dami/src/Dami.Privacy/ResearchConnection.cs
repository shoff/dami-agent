using System.Net;
using System.Net.Sockets;

namespace Dami.Privacy;

/// <summary>Connects research requests only to the public address the reader validated.</summary>
public static class ResearchConnection
{
    /// <summary>The validated address carried from DNS policy to the socket connection.</summary>
    public static HttpRequestOptionsKey<IPAddress> PinnedAddress { get; } = new("Dami.Research.PinnedAddress");

    /// <summary>Creates the credential-free, redirect-free research transport.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PooledConnectionLifetime = TimeSpan.Zero,
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.InitialRequestMessage.Options.TryGetValue(PinnedAddress, out var address))
        {
            throw new HttpRequestException("research request has no validated public address");
        }

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
