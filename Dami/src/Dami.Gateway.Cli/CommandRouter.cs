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

          dami beliefs [date]            what Dami believes, now or as of a date
          dami beliefs diff <from> [to]  what changed - the drift instrument (D-011)
          dami retract <id-prefix> <reason>
                                         stop believing something; the reason is recorded
          dami correct <id-prefix> <corrected statement>
                                         replace a belief - supersession, the audit trail kept
          dami note <text>               record an observation into the corpus
          dami approvals                 what awaits your yes or no
          dami approve <id-prefix>       approve (librarian manifests also execute)
          dami deny <id-prefix> [note]   deny; it never runs
          dami health                    check postgres, sidecars, GPU placement, tier
          dami stats                     vital signs: corpus, beliefs, passes, egress
          dami recall <query>            semantic search over everything Dami has seen
          dami ask <question>            answer from the corpus, with citations (local LLM)
          dami chat <message>            one full interactive turn - context, routing, traced
          dami chat --frontier <message> the same turn on your ChatGPT subscription
                                         (codex CLI, no API key); no memories are sent
          dami sessions                  list recent durable conversation sessions
          dami session start [id]        start a session (client-generated id when omitted)
          dami session resume <id>       resume an interrupted session
          dami session interrupt <id>    interrupt the session and any running turn
          dami session turn <id> <message>
                                         run a turn; prints reconnect key before sending
          dami session reconnect <id> <request-id>
                                         read durable turn state without re-executing
          dami frontier <question>       a bare question to the frontier via your subscription;
                                         no memories are sent (ADR-0011)
          dami brief <question>          draft a redacted, memory-informed brief for the
                                         frontier; egresses only after dami approve (C4)
          dami context <request>         show what would enter the prompt, and its token cost
          dami caption <image-path>      caption an image locally; it never leaves the host
          dami health-log                the structured health timeline (K2), local only
        """;

    /// <summary>Runs one command. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        InboxCommands inbox,
        TraceCommands traces,
        BeliefCommands beliefs,
        HealthCommands health,
        RecallCommands recall,
        AskCommands ask,
        ContextCommands contextCommands,
        VisionCommands vision,
        StatsCommands stats,
        ChatCommands chat,
        SessionCommands sessions,
        FrontierCommands frontier,
        ApprovalCommands approvals,
        BriefCommands briefs,
        HealthLogCommands healthLog)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(traces);
        ArgumentNullException.ThrowIfNull(beliefs);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(recall);
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(contextCommands);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(frontier);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(briefs);
        ArgumentNullException.ThrowIfNull(healthLog);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        return await DispatchAsync(
            args.Length == 0 ? "inbox" : args[0].ToLowerInvariant(),
            args, inbox, traces, beliefs, health, recall, ask, contextCommands, vision, stats,
            chat, sessions, frontier, approvals, briefs, healthLog, cancellation.Token)
            .ConfigureAwait(false);
    }

    private static async Task<int> DispatchAsync(
        string verb,
        string[] args,
        InboxCommands inbox,
        TraceCommands traces,
        BeliefCommands beliefs,
        HealthCommands health,
        RecallCommands recall,
        AskCommands ask,
        ContextCommands contextCommands,
        VisionCommands vision,
        StatsCommands stats,
        ChatCommands chat,
        SessionCommands sessions,
        FrontierCommands frontier,
        ApprovalCommands approvals,
        BriefCommands briefs,
        HealthLogCommands healthLog,
        CancellationToken cancellationToken)
    {
        return verb switch
        {
            "inbox" or "recent" or "read" or "good" or "bad" or "meh" =>
                await DispatchInboxAsync(verb, args, inbox, cancellationToken).ConfigureAwait(false),
            "trace" when args.Length > 1 =>
                await traces.ReplayAsync(args[1], cancellationToken).ConfigureAwait(false),
            "health" or "stats" or "health-log" =>
                await DispatchStatusAsync(verb, health, stats, healthLog, cancellationToken)
                    .ConfigureAwait(false),
            "recall" or "ask" or "context" or "caption" or "chat" when args.Length > 1 =>
                await DispatchModelAsync(verb, args, recall, ask, contextCommands, vision, chat,
                    cancellationToken).ConfigureAwait(false),
            "sessions" or "session" =>
                await SessionCommandRouter.RunAsync(args, sessions, cancellationToken)
                    .ConfigureAwait(false),
            "approvals" or "approve" or "deny" =>
                await DispatchApprovalsAsync(verb, args, approvals, cancellationToken).ConfigureAwait(false),
            "frontier" when args.Length > 1 =>
                await frontier.AskAsync(string.Join(' ', args[1..]), cancellationToken)
                    .ConfigureAwait(false),
            "brief" when args.Length > 1 =>
                await briefs.DraftAsync(string.Join(' ', args[1..]), cancellationToken)
                    .ConfigureAwait(false),
            "beliefs" or "correct" or "retract" or "note" =>
                await DispatchBeliefsAsync(verb, args, beliefs, cancellationToken).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static async Task<int> DispatchInboxAsync(
        string verb,
        string[] args,
        InboxCommands inbox,
        CancellationToken cancellationToken)
    {
        return verb switch
        {
            "inbox" => await inbox.ListPendingAsync(cancellationToken).ConfigureAwait(false),
            "recent" => await inbox.ListRecentAsync(cancellationToken).ConfigureAwait(false),
            "read" when args.Length > 1 =>
                await inbox.ReadAsync(args[1], cancellationToken).ConfigureAwait(false),
            "good" or "bad" or "meh" when args.Length > 1 =>
                await inbox.FeedbackAsync(
                    args[1], verb, args.Length > 2 ? string.Join(' ', args[2..]) : null,
                    cancellationToken).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static async Task<int> DispatchStatusAsync(
        string verb,
        HealthCommands health,
        StatsCommands stats,
        HealthLogCommands healthLog,
        CancellationToken cancellationToken)
    {
        return verb switch
        {
            "health" => await health.CheckAsync(cancellationToken).ConfigureAwait(false),
            "stats" => await stats.ShowAsync(cancellationToken).ConfigureAwait(false),
            _ => await healthLog.ShowAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<int> DispatchApprovalsAsync(
        string verb,
        string[] args,
        ApprovalCommands approvals,
        CancellationToken cancellationToken)
    {
        return verb switch
        {
            "approvals" => await approvals.ListAsync(cancellationToken).ConfigureAwait(false),
            "approve" when args.Length > 1 =>
                await approvals.ApproveAsync(args[1], cancellationToken).ConfigureAwait(false),
            "deny" when args.Length > 1 =>
                await approvals.DenyAsync(args[1], args.Length > 2 ? string.Join(' ', args[2..]) : null,
                    cancellationToken).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static async Task<int> DispatchModelAsync(
        string verb,
        string[] args,
        RecallCommands recall,
        AskCommands ask,
        ContextCommands contextCommands,
        VisionCommands vision,
        ChatCommands chat,
        CancellationToken cancellationToken)
    {
        var rest = string.Join(' ', args[1..]);
        return verb switch
        {
            "recall" => await recall.SearchAsync(rest, cancellationToken).ConfigureAwait(false),
            "ask" => await ask.AskAsync(rest, cancellationToken).ConfigureAwait(false),
            "context" => await contextCommands.ShowAsync(rest, cancellationToken).ConfigureAwait(false),
            "caption" => await vision.CaptionAsync(args[1], cancellationToken).ConfigureAwait(false),
            "chat" => rest.StartsWith("--frontier", StringComparison.Ordinal)
                ? await chat.FrontierTurnAsync(
                    rest["--frontier".Length..].Trim(), cancellationToken).ConfigureAwait(false)
                : await chat.TurnAsync(rest, cancellationToken).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static async Task<int> DispatchBeliefsAsync(
        string verb,
        string[] args,
        BeliefCommands beliefs,
        CancellationToken cancellationToken)
    {
        return verb switch
        {
            "beliefs" when args.Length > 2 && args[1] == "diff" =>
                await beliefs.DiffAsync(args[2], args.Length > 3 ? args[3] : null, cancellationToken)
                    .ConfigureAwait(false),
            "beliefs" =>
                await beliefs.ListAsync(args.Length > 1 ? args[1] : null, cancellationToken)
                    .ConfigureAwait(false),
            "correct" when args.Length > 2 =>
                await beliefs.CorrectAsync(args[1], string.Join(' ', args[2..]), cancellationToken)
                    .ConfigureAwait(false),
            "retract" when args.Length > 2 =>
                await beliefs.RetractAsync(args[1], string.Join(' ', args[2..]), cancellationToken)
                    .ConfigureAwait(false),
            "note" when args.Length > 1 =>
                await beliefs.NoteAsync(string.Join(' ', args[1..]), cancellationToken)
                    .ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.WriteLine(USAGE);
        return 2;
    }
}
