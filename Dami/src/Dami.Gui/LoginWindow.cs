using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dami.Authentication;

namespace Dami.Gui;

/// <summary>Collects the local account and runs the PKCE login.</summary>
/// <remarks>
/// A modal window rather than a browser hand-off: the host is on loopback and has no HTML
/// login page, so a browser would have nothing to show. The password exists only in this
/// window's textbox and the one POST to the host; the window keeps the granted token and
/// the caller decides where it goes.
/// </remarks>
public sealed class LoginWindow : Window
{
    private readonly Func<string, string, CancellationToken, Task<DevicePoll>> logIn;
    private readonly TextBox username = new() { PlaceholderText = "username" };
    private readonly TextBox password = new() { PlaceholderText = "password", PasswordChar = '•' };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button submit = new()
    {
        Content = "Log in",
        HorizontalAlignment = HorizontalAlignment.Right,
        IsDefault = true,
    };

    /// <summary>The granted token, once a login succeeds.</summary>
    public DamiToken? Token { get; private set; }

    /// <summary>Creates the window over the flow that performs the login.</summary>
    public LoginWindow(Func<string, string, CancellationToken, Task<DevicePoll>> logIn)
    {
        ArgumentNullException.ThrowIfNull(logIn);
        this.logIn = logIn;

        this.Title = "Log in to Dami";
        this.Width = 360;
        this.SizeToContent = SizeToContent.Height;
        this.CanResize = false;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.submit.Click += (_, _) => _ = this.SubmitAsync();
        this.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 8,
            Children = { this.username, this.password, this.message, this.submit },
        };
    }

    private async Task SubmitAsync()
    {
        var user = this.username.Text ?? string.Empty;
        var pass = this.password.Text ?? string.Empty;
        if (user.Length == 0 || pass.Length == 0)
        {
            this.Report("Both fields are required.");
            return;
        }

        this.submit.IsEnabled = false;
        try
        {
            DevicePoll poll = await this.logIn(user, pass, CancellationToken.None)
                .ConfigureAwait(true);
            if (poll.Result == DevicePollResult.Granted && poll.Token is not null)
            {
                this.Token = poll.Token;
                this.Close();
                return;
            }

            this.Report(poll.Error ?? poll.Result.ToString());
        }
        finally
        {
            this.submit.IsEnabled = true;
        }
    }

    private void Report(string text)
    {
        this.message.Text = text;
        this.message.IsVisible = true;
    }
}
