using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Dami.Capabilities.Mcp;

internal static class McpCapabilityIdentity
{
    private const int STACK_LIMIT = 512;

    public static Guid Create(Guid serverId, string toolName)
    {
        int nameBytes = Encoding.UTF8.GetByteCount(toolName);
        int inputLength = 16 + nameBytes;
        byte[]? rented = null;
        Span<byte> input = inputLength <= STACK_LIMIT
            ? stackalloc byte[inputLength]
            : (rented = ArrayPool<byte>.Shared.Rent(inputLength));
        try
        {
            serverId.TryWriteBytes(input[..16], bigEndian: true, out _);
            Encoding.UTF8.GetBytes(toolName, input[16..]);
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(input[..inputLength], hash);
            hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return new Guid(hash[..16], bigEndian: true);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static string AdvertisedName(Guid capabilityId)
    {
        return string.Create(36, capabilityId, static (destination, value) =>
        {
            "mcp_".AsSpan().CopyTo(destination);
            value.TryFormat(destination[4..], out _, "N");
        });
    }
}
