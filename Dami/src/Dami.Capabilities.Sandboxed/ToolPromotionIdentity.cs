using System.Security.Cryptography;

namespace Dami.Capabilities.Sandboxed;

internal static class ToolPromotionIdentity
{
    public static Guid Derive(Guid proposalId, string artifactVersion, byte discriminator)
    {
        Span<byte> input = stackalloc byte[81];
        input[0] = discriminator;
        proposalId.TryWriteBytes(input[1..17]);
        for (var index = 0; index < artifactVersion.Length; index++)
        {
            input[index + 17] = checked((byte)artifactVersion[index]);
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, digest);
        return new Guid(digest[..16]);
    }
}
