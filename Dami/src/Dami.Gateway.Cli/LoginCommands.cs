using Dami.Authentication;

namespace Dami.Gateway.Cli;

/// <summary>`dami login`, `logout`, `whoami` — the device flow, from a terminal.</summary>
/// <remarks>
/// The CLI is a public client and cannot hold a secret, so it never sees a password: the
/// user approves in a browser and the CLI receives only the resulting token. That is the
/// whole reason G5a chose the device flow for this client rather than a shared key in a
/// config file.
/// </remarks>
public sealed class LoginCommands
{
    private readonly DeviceLogin login;
    private readonly DamiTokenStore store;
    private readonly Uri host;
    private readonly TimeProvider clock;

    /// <summary>Creates the commands.</summary>
    public LoginCommands(DeviceLogin login, DamiTokenStore store, Uri host, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clock);

        this.login = login;
        this.store = store;
        this.host = host;
        this.clock = clock;
    }

    /// <summary>Runs the device flow and stores the token.</summary>
    public async Task<int> LogInAsync(CancellationToken cancellationToken)
    {
        DeviceAuthorization? authorization = await this.login
            .BeginAsync(this.host, cancellationToken)
            .ConfigureAwait(false);

        if (authorization is null)
        {
            await Console.Error.WriteLineAsync(
                $"{this.host} did not start a device authorization. Is authentication enabled on the host?")
                .ConfigureAwait(false);
            return 1;
        }

        await Console.Out.WriteLineAsync(DeviceLogin.Instructions(authorization)).ConfigureAwait(false);
        DevicePoll poll = await this.login
            .AwaitApprovalAsync(this.host, authorization, cancellationToken)
            .ConfigureAwait(false);

        return this.Record(poll);
    }

    private int Record(DevicePoll poll)
    {
        if (poll.Result != DevicePollResult.Granted || poll.Token is null)
        {
            Console.Error.WriteLine($"Login failed: {poll.Error ?? poll.Result.ToString()}");
            return 1;
        }

        this.store.Write(poll.Token);
        Console.WriteLine($"Logged in. Token stored at {this.store.Location}");
        return 0;
    }

    /// <summary>Forgets the stored token.</summary>
    public int LogOut()
    {
        this.store.Clear();
        Console.WriteLine("Logged out.");
        return 0;
    }

    /// <summary>Says whether there is a usable token, without printing it.</summary>
    /// <remarks>
    /// Never prints the token. A `whoami` that echoes a bearer token puts it in scrollback,
    /// in a screenshot, and in shell history the moment anyone pipes it anywhere.
    /// </remarks>
    public int WhoAmI()
    {
        DamiToken? token = this.store.Read();
        if (token is null)
        {
            Console.WriteLine("Not logged in. Run `dami login`.");
            return 1;
        }

        if (token.IsExpiredAt(this.clock.GetUtcNow()))
        {
            Console.WriteLine("Token expired. Run `dami login`.");
            return 1;
        }

        var left = token.ObtainedAt == default
            ? "unknown"
            : $"{(token.ObtainedAt + token.ExpiresIn - this.clock.GetUtcNow()).TotalMinutes:F0} min";
        Console.WriteLine($"Logged in ({token.AccessToken.Length} char token, {left} left).");
        return 0;
    }
}
