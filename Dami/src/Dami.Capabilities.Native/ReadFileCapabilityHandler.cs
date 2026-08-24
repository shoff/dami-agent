using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Reads one UTF-8 file beneath a configured local root.</summary>
[NativeCapability(
    "946a3c12-84a5-4cc3-a497-957aaf4d5b6d",
    "read-file",
    "Read one UTF-8 text file beneath the configured workspace root.",
    "native://read-file/schema/v1",
    "1.0.0",
    Tags = new[] { "files", "read" })]
public sealed class ReadFileCapabilityHandler : INativeCapabilityHandler
{
    private const int ABSOLUTE_MAX_BYTES = 4 * 1024 * 1024;

    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    private readonly int maxBytes;
    private readonly RootedPathResolver pathResolver;

    /// <summary>Creates the root-confined file reader.</summary>
    public ReadFileCapabilityHandler(ReadFileCapabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxBytes is <= 0 or > ABSOLUTE_MAX_BYTES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxBytes, $"MaxBytes must be between 1 and {ABSOLUTE_MAX_BYTES}.");
        }

        this.pathResolver = new RootedPathResolver(options.RootDirectory);
        this.maxBytes = options.MaxBytes;
    }

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var relativePath = ReadRelativePath(arguments);
        var fullPath = this.pathResolver.ResolveFile(relativePath);
        return await this.ReadAsync(relativePath, fullPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CapabilityExecutionResult> ReadAsync(
        string relativePath,
        string fullPath,
        CancellationToken cancellationToken)
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

        var content = ArrayPool<byte>.Shared.Rent(this.maxBytes + 1);
        try
        {
            var bytesRead = await ReadUpToAsync(stream, content, cancellationToken).ConfigureAwait(false);
            return this.CreateResult(relativePath, content, bytesRead);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(content, clearArray: true);
        }
    }

    private CapabilityExecutionResult CreateResult(string relativePath, byte[] content, int bytesRead)
    {
        if (bytesRead > this.maxBytes)
        {
            throw this.CreateTooLargeException();
        }

        var bytes = content.AsSpan(0, bytesRead);
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["path"] = relativePath,
            ["bytes"] = bytesRead.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)),
        };
        return new CapabilityExecutionResult(strictUtf8.GetString(bytes), evidence);
    }

    private InvalidDataException CreateTooLargeException()
    {
        return new InvalidDataException($"Read-file input exceeds the configured limit of {this.maxBytes} bytes.");
    }

    private static async Task<int> ReadUpToAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static string ReadRelativePath(JsonElement arguments)
    {
        if (arguments.TryGetProperty("path", out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { Length: > 0 } path)
        {
            return path;
        }

        throw new ArgumentException("Read-file arguments require a non-empty string 'path'.", nameof(arguments));
    }

}
