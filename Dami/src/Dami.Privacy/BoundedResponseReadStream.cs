namespace Dami.Privacy;

/// <summary>Limits bytes consumed from a streamed egress response and owns its content.</summary>
internal sealed class BoundedResponseReadStream : Stream
{
    private readonly Stream inner;
    private readonly HttpContent owner;
    private readonly int maxBytes;
    private int bytesRead;

    public BoundedResponseReadStream(Stream inner, HttpContent owner, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(owner);
        this.inner = inner;
        this.owner = owner;
        this.maxBytes = maxBytes;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return this.Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        int read = this.inner.Read(this.Limit(buffer));
        return this.Record(read);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await this.inner.ReadAsync(
            this.Limit(buffer), cancellationToken).ConfigureAwait(false);
        return this.Record(read);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return this.ReadArrayAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.owner.Dispose();
        }

        base.Dispose(disposing);
    }

    private Span<byte> Limit(Span<byte> buffer) => buffer[..this.Limit(buffer.Length)];

    private Memory<byte> Limit(Memory<byte> buffer) => buffer[..this.Limit(buffer.Length)];

    private int Limit(int requested)
    {
        return requested == 0 ? 0 : Math.Min(requested, this.maxBytes - this.bytesRead + 1);
    }

    private int Record(int read)
    {
        this.bytesRead = checked(this.bytesRead + read);
        if (this.bytesRead > this.maxBytes)
        {
            throw new InvalidDataException(
                $"MCP response exceeds {this.maxBytes} bytes.");
        }

        return read;
    }

    private async Task<int> ReadArrayAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        return await this.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
}
