using Dami.Contracts.TaskBoard;

namespace Dami.Gateway.Cli;

/// <summary>Parses the board verb family: the board is where work is claimed now.</summary>
public static class BoardCommandRouter
{
    private const string USAGE = """
        usage: dami board                          list boards
               dami board <board> [--open]         the task tree (--open hides finished work)
               dami board claim <id8> [note]       claim a task as $DAMI_ACTOR
               dami board complete <id8> [note]    complete a task you hold
               dami board block|reopen|cancel <id8> <reason>
               dami board criterion <id8> yes|no   mark an acceptance criterion
               dami board add <id8|board> <title> [--needs <criterion>]...
                                                   add a task under a task, or at a board's root
               dami board-import <TODO.md> --revision <sha> --actor <id> [--agent] [--dry-run]
        """;

    /// <summary>Dispatches one board command. <paramref name="args"/> starts at the verb.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        BoardCommands board,
        BoardImportCommands import,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(import);
        if (args.Length > 0 && string.Equals(args[0], "board-import", StringComparison.Ordinal))
        {
            return await import.ImportAsync(args[1..], cancellationToken).ConfigureAwait(false);
        }

        return args.Length switch
        {
            1 => await board.ListAsync(cancellationToken).ConfigureAwait(false),
            _ => await DispatchAsync(args[1..], board, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<int> DispatchAsync(
        string[] rest,
        BoardCommands board,
        CancellationToken cancellationToken)
    {
        var note = rest.Length > 2 ? string.Join(' ', rest[2..]) : null;
        return rest[0] switch
        {
            "claim" when rest.Length >= 2 =>
                await board.ClaimAsync(rest[1], note, cancellationToken).ConfigureAwait(false),
            "complete" when rest.Length >= 2 =>
                await board.CompleteAsync(rest[1], note, cancellationToken).ConfigureAwait(false),
            "block" when note is not null =>
                await board.SetStatusAsync(rest[1], TaskBoardStatus.Blocked, note, cancellationToken).ConfigureAwait(false),
            "reopen" when note is not null =>
                await board.SetStatusAsync(rest[1], TaskBoardStatus.Open, note, cancellationToken).ConfigureAwait(false),
            "cancel" when note is not null =>
                await board.SetStatusAsync(rest[1], TaskBoardStatus.Cancelled, note, cancellationToken).ConfigureAwait(false),
            "criterion" when rest.Length == 3 && rest[2] is "yes" or "no" =>
                await board.SetCriterionAsync(rest[1], rest[2] == "yes", cancellationToken).ConfigureAwait(false),
            "add" when rest.Length >= 3 => await AddAsync(rest, board, cancellationToken).ConfigureAwait(false),
            "claim" or "complete" or "block" or "reopen" or "cancel" or "criterion" or "add" => Usage(),
            _ => await board.ShowAsync(rest[0], rest.Contains("--open"), cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>`add TARGET TITLE-WORDS [--needs CRITERION]…` — the title ends at the first --needs.</summary>
    private static Task<int> AddAsync(string[] rest, BoardCommands board, CancellationToken cancellationToken)
    {
        var titleWords = new List<string>();
        var criteria = new List<string>();
        var current = titleWords;
        foreach (var word in rest[2..])
        {
            if (word == "--needs")
            {
                current = [];
                criteria.Add(string.Empty);
                continue;
            }

            if (ReferenceEquals(current, titleWords))
            {
                titleWords.Add(word);
            }
            else
            {
                criteria[^1] = (criteria[^1] + " " + word).Trim();
            }
        }

        return titleWords.Count == 0 || criteria.Any(string.IsNullOrWhiteSpace)
            ? Task.FromResult(Usage())
            : board.AddAsync(rest[1], string.Join(' ', titleWords), criteria, cancellationToken);
    }

    private static int Usage()
    {
        Console.WriteLine(USAGE);
        return 2;
    }
}
