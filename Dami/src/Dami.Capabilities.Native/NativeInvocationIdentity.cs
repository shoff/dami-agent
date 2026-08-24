using System.Security.Cryptography;

namespace Dami.Capabilities.Native;

internal static class NativeInvocationIdentity
{
    public static Guid Derive(Guid namespaceId, Guid traceId, Guid spanId)
    {
        Span<byte> input = stackalloc byte[48];
        namespaceId.TryWriteBytes(input[..16]);
        traceId.TryWriteBytes(input[16..32]);
        spanId.TryWriteBytes(input[32..]);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
