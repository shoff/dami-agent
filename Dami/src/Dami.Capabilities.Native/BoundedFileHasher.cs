using System.Buffers;
using System.Security.Cryptography;

namespace Dami.Capabilities.Native;

/// <summary>Hashes a file under a hard byte cap, including concurrent growth.</summary>
internal sealed class BoundedFileHasher
{
    public const int ABSOLUTE_MAX_BYTES = 4 * 1024 * 1024;

    private const int BUFFER_BYTES = 64 * 1024;

    private readonly int maxBytes;

    public BoundedFileHasher(int maxBytes)
    {
        if (maxBytes is <= 0 or > ABSOLUTE_MAX_BYTES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes), maxBytes,
                $"The byte bound must be between 1 and {ABSOLUTE_MAX_BYTES}.");
        }

        this.maxBytes = maxBytes;
    }

    public async Task<string> HashAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(fullPath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            Share = FileShare.Read,
        });
        if (stream.Length > this.maxBytes)
        {
            throw this.CreateTooLargeException();
        }

        return await this.HashAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> HashAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(BUFFER_BYTES, this.maxBytes + 1));
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            var total = 0;
            int read;
            do
            {
                var readLength = Math.Min(buffer.Length, this.maxBytes - total + 1);
                read = await stream.ReadAsync(
                    buffer.AsMemory(0, readLength), cancellationToken).ConfigureAwait(false);
                total += read;
                if (total > this.maxBytes)
                {
                    throw this.CreateTooLargeException();
                }

                hasher.AppendData(buffer, 0, read);
            }
            while (read > 0);

            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            hasher.TryGetHashAndReset(hash, out _);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private InvalidDataException CreateTooLargeException()
    {
        return new InvalidDataException(
            $"Current file exceeds the configured limit of {this.maxBytes} bytes.");
    }
}
