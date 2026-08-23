namespace Dami.Gateway.Cli;

/// <summary>Dispatches the command line. Hand-rolled: the surface is five verbs.</summary>
public static class CommandRouter
{
    private const string USAGE = """
        dami - the queue you read when you want to

          dami inbox                     pending surfacings
          dami read <id-prefix>          show one in full and mark it delivered
          dami good|bad|meh <id-prefix> [note]
                                         record feedback - this trains the taste model
          dami recent                    recent surfacings in every status
          dami trace <trace-id>          replay one trace from the event store
        """;

    /// <summary>Runs one command. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args, InboxCommands inbox, TraceCommands traces)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(traces);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var verb = args.Length == 0 ? "inbox" : args[0].ToLowerInvariant();

        return verb switch
        {
            "inbox" => await inbox.ListPendingAsync(cancellation.Token).ConfigureAwait(false),
            "recent" => await inbox.ListRecentAsync(cancellation.Token).ConfigureAwait(false),
            "read" when args.Length > 1 =>
                await inbox.ReadAsync(args[1], cancellation.Token).ConfigureAwait(false),
            "good" or "bad" or "meh" when args.Length > 1 =>
                await inbox.FeedbackAsync(
                    args[1], verb, args.Length > 2 ? string.Join(' ', args[2..]) : null,
                    cancellation.Token).ConfigureAwait(false),
            "trace" when args.Length > 1 =>
                await traces.ReplayAsync(args[1], cancellation.Token).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine(USAGE);
        return 2;
    }
}
