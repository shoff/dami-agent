using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>
/// A file remembering the last period something spoke for, so a restart does not repeat it.
/// Unreadable or unwritable is a warning, never a failure: on 2026-09-29 a root-owned folder
/// stopped the reliability notice entirely.
/// </summary>
internal sealed class DayMarker
{
    private readonly string path;
    private readonly ILogger logger;

    public DayMarker(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        this.path = path;
        this.logger = logger;
    }

    /// <summary><c>~/.local/state/dami/&lt;name&gt;</c>.</summary>
    public static string Default(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "dami", name);

    /// <summary>The last marked instant, or the minimum when none.</summary>
    public DateTimeOffset Read()
    {
        try
        {
            return File.Exists(this.path)
                && DateTimeOffset.TryParse(File.ReadAllText(this.path).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                ? at
                : DateTimeOffset.MinValue;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            this.logger.LogWarning(exception, "Could not read {Path}; treating it as unmarked", this.path);
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>Marks <paramref name="at"/>.</summary>
    public void Write(DateTimeOffset at)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            File.WriteAllText(this.path, at.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            this.logger.LogWarning(exception, "Could not write {Path}; a restart may repeat this", this.path);
        }
    }
}
