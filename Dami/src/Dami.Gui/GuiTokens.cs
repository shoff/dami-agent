using Dami.Authentication;

namespace Dami.Gui;

/// <summary>The desktop client's stored login.</summary>
/// <remarks>
/// Its own file rather than the CLI's token.json: tokens are bound to the client
/// registration that earned them, and dami-gui and dami-cli are different registrations.
/// Sharing one file would make either client's logout silently sign the other out.
/// </remarks>
public static class GuiTokens
{
    /// <summary>The GUI's on-disk token store.</summary>
    public static DamiTokenStore Store() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "dami", "gui-token.json"),
        TimeProvider.System);

    /// <summary>The token to send, if any. A stored login beats the environment.</summary>
    public static string? Access()
    {
        var token = Store().Read();
        return token is not null && !token.IsExpiredAt(TimeProvider.System.GetUtcNow())
            ? token.AccessToken
            : Environment.GetEnvironmentVariable("DAMI_ACCESS_TOKEN");
    }
}
