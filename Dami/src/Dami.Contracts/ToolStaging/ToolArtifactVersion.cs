using System.Security.Cryptography;

namespace Dami.Contracts.ToolStaging;

internal static class ToolArtifactVersion
{
    public static void Validate(string artifactVersion, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(artifactVersion, parameterName);
        if (artifactVersion.Length != SHA256.HashSizeInBytes * 2)
        {
            throw Invalid(parameterName);
        }

        for (var index = 0; index < artifactVersion.Length; index++)
        {
            char character = artifactVersion[index];
            if (!char.IsAsciiDigit(character) && character is < 'a' or > 'f')
            {
                throw Invalid(parameterName);
            }
        }
    }

    private static ArgumentException Invalid(string parameterName)
    {
        return new ArgumentException(
            "An artifact version must be a lowercase SHA-256 value.", parameterName);
    }
}
