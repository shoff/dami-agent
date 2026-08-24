namespace Dami.Contracts.Capabilities;

internal static class SkillVersion
{
    public static bool IsCanonical(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsLowerHex(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerHex(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f';
    }
}
