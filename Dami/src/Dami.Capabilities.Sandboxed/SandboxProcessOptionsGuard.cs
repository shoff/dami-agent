namespace Dami.Capabilities.Sandboxed;

internal static class SandboxProcessOptionsGuard
{
    public static void Validate(SandboxProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxInputBytes is <= 0 or > 4_194_304
            || options.MaxOutputBytes is <= 0 or > 4_194_304
            || options.MemoryMaxBytes is < 67_108_864 or > 17_179_869_184
            || options.ProcessMax is < 1 or > 256
            || options.RuntimeMax < TimeSpan.FromSeconds(1)
            || options.RuntimeMax > TimeSpan.FromMinutes(5)
            || options.RuntimeMax.Ticks % TimeSpan.TicksPerSecond != 0
            || !Path.IsPathFullyQualified(options.UserRuntimeDirectory))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Sandbox limits or the user runtime path are invalid.");
        }
    }
}
