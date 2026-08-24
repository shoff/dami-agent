using System.Buffers;
using System.Text;

namespace Dami.Capabilities.Processes;

internal sealed class BoundedProcessOutput : IDisposable
{
    private readonly byte[] buffer;
    private int count;

    public BoundedProcessOutput(int maxOutputBytes)
    {
        this.buffer = ArrayPool<byte>.Shared.Rent(maxOutputBytes + 1);
    }

    public async Task CaptureAsync(
        Stream stream,
        SharedOutputBudget budget,
        CancellationTokenSource stop)
    {
        while (true)
        {
            var read = await stream.ReadAsync(
                this.buffer.AsMemory(this.count), stop.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            if (!budget.TryConsume(read))
            {
                await stop.CancelAsync().ConfigureAwait(false);
                throw new OutputLimitExceededException();
            }

            this.count += read;
        }
    }

    public string Decode(Encoding encoding)
    {
        return encoding.GetString(this.buffer.AsSpan(0, this.count));
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(this.buffer, clearArray: true);
    }
}

internal sealed class SharedOutputBudget
{
    private readonly int maximum;
    private int consumed;
    private int exceeded;

    public SharedOutputBudget(int maximum)
    {
        this.maximum = maximum;
    }

    public bool Exceeded => Volatile.Read(ref this.exceeded) != 0;

    public bool TryConsume(int count)
    {
        while (true)
        {
            var current = Volatile.Read(ref this.consumed);
            if (count > this.maximum - current)
            {
                Interlocked.Exchange(ref this.exceeded, 1);
                return false;
            }

            if (Interlocked.CompareExchange(ref this.consumed, current + count, current) == current)
            {
                return true;
            }
        }
    }
}

internal sealed class OutputLimitExceededException : Exception;
