using Dami.Authentication;

namespace Dami.Gui;

/// <summary>Interactive login, offered only when the runtime demands it.</summary>
public sealed partial class MainWindow
{
    /// <summary>Offers a login when the runtime turns this client away.</summary>
    /// <remarks>
    /// Probed rather than assumed: with authentication disabled on the host the window
    /// must never ask, and with it enabled the rest of the window is already useless —
    /// every poll answers 401 — so a modal prompt hides nothing that was working.
    /// </remarks>
    private async Task EnsureLoggedInAsync()
    {
        if (!await this.runtime.IsUnauthorizedAsync(this.lifetime.Token).ConfigureAwait(true))
        {
            return;
        }

        var login = new PkceLogin(PkceLogin.CreateHttpClient());
        var host = new Uri(RuntimeClient.BASE_URL);
        var redirect = new Uri(DamiAuthenticationOptions.DEFAULT_GUI_REDIRECT_URI);
        var window = new LoginWindow((user, pass, cancellationToken) =>
            login.LogInAsync(host, redirect, user, pass, cancellationToken));
        await window.ShowDialog(this).ConfigureAwait(true);

        if (window.Token is { } token)
        {
            GuiTokens.Store().Write(token);
            this.runtime.Authenticate(token.AccessToken);
            this.taskBoardClient.Authenticate(token.AccessToken);
        }
    }
}
