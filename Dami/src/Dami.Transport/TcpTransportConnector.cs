using System.Net;
using Dami.Contracts.Transport;

namespace Dami.Transport;

/// <summary>Creates heartbeat-enabled framed transports over fresh TCP connections.</summary>
public sealed class TcpTransportConnector : ITransportConnector
{
    private readonly IPEndPoint endpoint;
    private readonly TimeSpan heartbeatInterval;
    private readonly TimeSpan silenceTimeout;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a reusable connector for one remote endpoint.</summary>
    public TcpTransportConnector(
        IPEndPoint endpoint,
        TimeSpan heartbeatInterval,
        TimeSpan silenceTimeout,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(timeProvider);
        HeartbeatTransport.ValidateTiming(heartbeatInterval, silenceTimeout);
        this.endpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
        this.heartbeatInterval = heartbeatInterval;
        this.silenceTimeout = silenceTimeout;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async ValueTask<ITransport> ConnectAsync(CancellationToken cancellationToken)
    {
        TcpDuplexPipe connection = await TcpDuplexPipe.ConnectAsync(
            this.endpoint,
            cancellationToken).ConfigureAwait(false);
        try
        {
            var pipeTransport = new PipeTransport(connection);
            return new HeartbeatTransport(
                pipeTransport,
                this.heartbeatInterval,
                this.silenceTimeout,
                this.timeProvider);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
