namespace Dami.Providers;

/// <summary>What a finished command reported.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Output">Standard output followed by standard error, trimmed.</param>
public sealed record CommandResult(int ExitCode, string Output);
