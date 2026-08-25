using System.Security.Cryptography;
using System.Text;

namespace Dami.Core.TaskBoard;

internal static class StablePlanningId
{
    internal static Guid Create(Guid requestId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var input = Encoding.UTF8.GetBytes($"{requestId:N}:{key}");
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        digest[6] = (byte)((digest[6] & 0x0F) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3F) | 0x80);
        return new Guid(digest[..16]);
    }
}
