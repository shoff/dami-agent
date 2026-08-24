namespace Dami.Gateway.Cli;

/// <summary>Parses the session verb family without coupling commands to process startup.</summary>
public static class SessionCommandRouter
{
    /// <summary>Dispatches one session command.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        SessionCommands commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(commands);
        if (args is ["sessions"])
        {
            return await commands.ListAsync(cancellationToken).ConfigureAwait(false);
        }

        if (args.Length < 2 || !string.Equals(args[0], "session", StringComparison.Ordinal))
        {
            return await UsageAsync().ConfigureAwait(false);
        }

        return await DispatchAsync(args, commands, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> DispatchAsync(
        string[] args,
        SessionCommands commands,
        CancellationToken cancellationToken)
    {
        return args[1] switch
        {
            "start" when args.Length <= 3 =>
                await commands.StartAsync(args.Length == 3 ? args[2] : null, cancellationToken)
                    .ConfigureAwait(false),
            "resume" when args.Length == 3 =>
                await commands.ResumeAsync(args[2], cancellationToken).ConfigureAwait(false),
            "interrupt" when args.Length == 3 =>
                await commands.InterruptAsync(args[2], cancellationToken).ConfigureAwait(false),
            "turn" when args.Length > 3 =>
                await commands.TurnAsync(
                    args[2], string.Join(' ', args, 3, args.Length - 3), cancellationToken)
                    .ConfigureAwait(false),
            "reconnect" when args.Length == 4 =>
                await commands.ReconnectAsync(args[2], args[3], cancellationToken)
                    .ConfigureAwait(false),
            _ => await UsageAsync().ConfigureAwait(false),
        };
    }

    private static async Task<int> UsageAsync()
    {
        await Console.Out.WriteLineAsync(
            "usage: dami sessions | dami session start [id] | resume|interrupt <id> | turn <id> <message> | reconnect <id> <request-id>")
            .ConfigureAwait(false);
        return 2;
    }
}
