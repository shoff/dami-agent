using System.Text.Json;

namespace Dami.Authentication;

/// <summary>An acquired token and what it is good for.</summary>
public sealed record DamiToken(string AccessToken, string? RefreshToken, TimeSpan ExpiresIn)
{
    /// <summary>When it was obtained. Set on write, so expiry can be judged on read.</summary>
    public DateTimeOffset ObtainedAt { get; init; }

    /// <summary>Whether it is past use, with a minute of slack for a turn in flight.</summary>
    public bool IsExpiredAt(DateTimeOffset now) =>
        this.ObtainedAt != default && now >= this.ObtainedAt + this.ExpiresIn - TimeSpan.FromMinutes(1);
}

/// <summary>Keeps the CLI's token on disk between invocations.</summary>
/// <remarks>
/// The CLI is a process per command, so a token that lived in memory would mean a device
/// login before every verb. It goes under the user's config directory at 0600 and never
/// near the repository — a token in the working tree is a secret in version control one
/// `git add -A` later, which this repository has already done once with someone else's
/// files.
/// </remarks>
public sealed class DamiTokenStore
{
    private static readonly JsonSerializerOptions format = new() { WriteIndented = true };

    private readonly string path;
    private readonly TimeProvider clock;

    /// <summary>Creates a store at the default location.</summary>
    public DamiTokenStore(TimeProvider clock)
        : this(DefaultPath(), clock)
    {
    }

    /// <summary>Creates a store at an explicit path, for tests.</summary>
    public DamiTokenStore(string path, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(clock);

        this.path = path;
        this.clock = clock;
    }

    /// <summary>Where the token lives.</summary>
    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "dami", "token.json");

    /// <summary>Where this store is reading and writing.</summary>
    public string Location => this.path;

    /// <summary>Reads the stored token, or null if there is none or it is unreadable.</summary>
    public DamiToken? Read()
    {
        try
        {
            return File.Exists(this.path)
                ? JsonSerializer.Deserialize<DamiToken>(File.ReadAllText(this.path))
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable token is the same as no token: log in again.
            return null;
        }
    }

    /// <summary>Writes the token, stamped with now, readable only by its owner.</summary>
    public void Write(DamiToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var directory = System.IO.Path.GetDirectoryName(this.path)!;
        Directory.CreateDirectory(directory);
        var stamped = token with { ObtainedAt = this.clock.GetUtcNow() };
        File.WriteAllText(this.path, JsonSerializer.Serialize(stamped, format));
        Protect(this.path);
    }

    /// <summary>Forgets the token.</summary>
    public void Clear()
    {
        if (File.Exists(this.path))
        {
            File.Delete(this.path);
        }
    }

    /// <remarks>
    /// Owner-only, and set after the write rather than before: the file is created by the
    /// write, so permissions applied first would be applied to nothing.
    /// </remarks>
    private static void Protect(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
