using Dami.Contracts.TaskBoard;

namespace Dami.Core.BoardImport;

/// <summary>The one legal move an import can make on a task right now.</summary>
public enum ImportStepKind
{
    /// <summary>The board already says what the file says, or says something newer.</summary>
    None,

    /// <summary>Take the task so it can be advanced.</summary>
    Claim,

    /// <summary>Tick the acceptance criteria completion requires.</summary>
    SatisfyCriteria,

    /// <summary>Finish it.</summary>
    Complete,

    /// <summary>Mark it blocked, with the reason.</summary>
    Block,

    /// <summary>Cancel it — the file says <c>[-]</c>.</summary>
    Cancel,

    /// <summary>Reopen it: the board has it blocked and the file has moved past that.</summary>
    Reopen,

    /// <summary>The file and the board disagree in a way the import must not resolve itself.</summary>
    Conflict,
}

/// <summary>What to do next about one task, and who should do it.</summary>
/// <param name="Kind">The move.</param>
/// <param name="Actor">Who makes it.</param>
/// <param name="Detail">The reason for a block, or the description of a conflict.</param>
public sealed record ImportStep(ImportStepKind Kind, TaskActor Actor, string Detail)
{
    /// <summary>
    /// Decides the next move for one task, advancing only.
    /// </summary>
    /// <remarks>
    /// The rule that matters on a rerun: the file is a snapshot someone edits by hand, and
    /// the board is live. Work finished on the board while the file still says open is the
    /// normal case, not the exception, so a state the board has already reached is never
    /// pulled backwards. Where the file claims something the board contradicts, the import
    /// reports it and changes nothing.
    /// </remarks>
    /// <param name="desired">What the file says.</param>
    /// <param name="actual">What the board says.</param>
    /// <param name="importer">The actor running the import.</param>
    /// <returns>The next move.</returns>
    public static ImportStep Next(DesiredTask desired, BoardTask actual, TaskActor importer)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(importer);

        if (Blocking(desired) is { } reason)
        {
            return Block(desired, actual, importer, reason);
        }

        return desired.State switch
        {
            TodoState.Done => Finish(desired, actual, importer),
            TodoState.InProgress => Start(desired, actual, importer),
            TodoState.Cancelled => Cancel(desired, actual, importer),
            _ => new ImportStep(ImportStepKind.None, importer, string.Empty),
        };
    }

    /// <summary>The reason a task should be blocked, if the file gives one.</summary>
    private static string? Blocking(DesiredTask desired)
    {
        if (desired.BlockedReason is not null)
        {
            return $"Blocked in TODO.md: {desired.BlockedReason}";
        }

        return desired.State switch
        {
            TodoState.NeedsSteve => "Waiting on Steve's key or decision.",
            TodoState.Deferred =>
                $"Deferred in TODO.md, not cancelled: {desired.Detail ?? "no reason given"}",
            _ => null,
        };
    }

    private static ImportStep Block(
        DesiredTask desired,
        BoardTask actual,
        TaskActor importer,
        string reason)
    {
        return actual.Status switch
        {
            TaskBoardStatus.Blocked => None(importer),
            TaskBoardStatus.Open => new ImportStep(ImportStepKind.Block, importer, reason),
            TaskBoardStatus.InProgress when Holds(actual, importer)
                => new ImportStep(ImportStepKind.Block, importer, reason),
            _ => Conflict(desired, actual, importer),
        };
    }

    private static ImportStep Cancel(DesiredTask desired, BoardTask actual, TaskActor importer)
    {
        return actual.Status switch
        {
            TaskBoardStatus.Cancelled => None(importer),
            TaskBoardStatus.Open or TaskBoardStatus.Blocked
                => new ImportStep(ImportStepKind.Cancel, importer, "Cancelled in TODO.md."),
            TaskBoardStatus.InProgress when Holds(actual, importer)
                => new ImportStep(ImportStepKind.Cancel, importer, "Cancelled in TODO.md."),
            _ => Conflict(desired, actual, importer),
        };
    }

    private static ImportStep Finish(DesiredTask desired, BoardTask actual, TaskActor importer)
    {
        switch (actual.Status)
        {
            case TaskBoardStatus.Done:
                return None(importer);
            case TaskBoardStatus.Open:
                return new ImportStep(ImportStepKind.Claim, Owner(desired, importer), string.Empty);
            case TaskBoardStatus.Blocked:
                return new ImportStep(ImportStepKind.Reopen, importer, "Done in TODO.md; reopened to finish it.");
            case TaskBoardStatus.InProgress when Holds(actual, importer):
                return actual.AcceptanceCriteria.Any(criterion => !criterion.IsSatisfied)
                    ? new ImportStep(ImportStepKind.SatisfyCriteria, importer, string.Empty)
                    : new ImportStep(ImportStepKind.Complete, importer, string.Empty);
            default:
                return Conflict(desired, actual, importer);
        }
    }

    private static ImportStep Start(DesiredTask desired, BoardTask actual, TaskActor importer)
    {
        return actual.Status switch
        {
            TaskBoardStatus.Blocked => new ImportStep(ImportStepKind.Reopen, importer, "Claimed in TODO.md; reopened to claim it."),
            TaskBoardStatus.InProgress => None(importer),
            TaskBoardStatus.Open => new ImportStep(
                ImportStepKind.Claim, Owner(desired, importer), string.Empty),
            _ => Conflict(desired, actual, importer),
        };
    }

    /// <summary>
    /// Whoever the file names, so an imported claim reads as the person who made it rather
    /// than as the importer. Unowned work is claimed by the importer, and completion
    /// requires the claimant, so the two must agree.
    /// </summary>
    private static TaskActor Owner(DesiredTask desired, TaskActor importer)
    {
        if (desired.Owner is null)
        {
            return importer;
        }

        var actorId = desired.Owner.ToLowerInvariant();
        return new TaskActor(
            actorId,
            actorId == "steve" ? TaskActorKind.Human : TaskActorKind.Agent);
    }

    private static bool Holds(BoardTask actual, TaskActor importer)
    {
        return actual.Claim is not null
            && string.Equals(actual.Claim.Actor.ActorId, importer.ActorId, StringComparison.Ordinal);
    }

    private static ImportStep None(TaskActor importer)
    {
        return new ImportStep(ImportStepKind.None, importer, string.Empty);
    }

    private static ImportStep Conflict(DesiredTask desired, BoardTask actual, TaskActor importer)
    {
        var held = actual.Claim is null ? "unclaimed" : $"claimed by {actual.Claim.Actor.ActorId}";
        return new ImportStep(
            ImportStepKind.Conflict,
            importer,
            $"{desired.TodoId ?? actual.Title} is {desired.State} in TODO.md but {actual.Status} "
                + $"({held}) on the board; left as it is.");
    }
}
