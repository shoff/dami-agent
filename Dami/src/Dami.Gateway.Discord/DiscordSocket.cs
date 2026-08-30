using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace Dami.Gateway.Discord;

/// <summary>The real gateway socket, over <see cref="ClientWebSocket"/>.</summary>
/// <remarks>
/// Reassembly is the whole job here. A gateway frame arrives in as many WebSocket frames
/// as Discord feels like using, and treating the first chunk as the message is the
/// classic hand-rolled-transport bug D-013 warned about: it works for every small payload
/// in testing and fails on the first large READY.
/// </remarks>
public sealed class DiscordSocket : IDiscordSocket
{
    private const int CHUNK = 8192;

    private readonly ClientWebSocket socket = new();

    /// <inheritdoc />
    public DiscordClose? CloseReason { get; private set; }

    /// <inheritdoc />
    public Task ConnectAsync(Uri gateway, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        return this.socket.ConnectAsync(gateway, cancellationToken);
    }

    /// <inheritdoc />
    public Task SendAsync(string json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        return this.socket.SendAsync(
            Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CHUNK);
        try
        {
            var message = new ArrayBufferWriter<byte>();
            WebSocketReceiveResult result;
            do
            {
                result = await this.socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    this.CloseReason = new DiscordClose(
                        (int?)this.socket.CloseStatus ?? 0,
                        this.socket.CloseStatusDescription ?? string.Empty);
                    return null;
                }

                message.Write(buffer.AsSpan(0, result.Count));
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(message.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
