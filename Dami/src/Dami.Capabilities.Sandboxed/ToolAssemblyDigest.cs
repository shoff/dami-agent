using System.Security.Cryptography;

namespace Dami.Capabilities.Sandboxed;

internal static class ToolAssemblyDigest
{
    public static async Task<string> ComputeAsync(
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            assemblyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }
}
