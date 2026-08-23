using System.IO.Pipelines;

namespace Dami.Transport.Tests;

internal sealed class ThrowingCompletePipeReader : PipeReader
{
    public override void AdvanceTo(SequencePosition consumed)
    {
        throw new NotSupportedException();
    }

    public override void AdvanceTo(
        SequencePosition consumed,
        SequencePosition examined)
    {
        throw new NotSupportedException();
    }

    public override void CancelPendingRead()
    {
    }

    public override void Complete(Exception? exception = null)
    {
        throw new InvalidOperationException("Input completion failed.");
    }

    public override ValueTask<ReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public override bool TryRead(out ReadResult result)
    {
        result = default;
        return false;
    }
}
