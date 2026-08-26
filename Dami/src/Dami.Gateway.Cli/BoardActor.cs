using Dami.Contracts.TaskBoard;

namespace Dami.Gateway.Cli;

/// <summary>Who the CLI acts as on the task board.</summary>
/// <remarks>
/// Client-asserted until G5a2 replaces it with validated claims. <c>DAMI_ACTOR</c> names
/// the actor and <c>DAMI_ACTOR_KIND</c> (<c>Human</c> or <c>Agent</c>) its kind; absent
/// both, the login user acts as a human. An agent must say so — a claim that looks human
/// when it was not is the one misattribution the ledger cannot recover from.
/// </remarks>
public static class BoardActor
{
    /// <summary>Resolves the actor from the environment.</summary>
    public static TaskActor FromEnvironment()
    {
        var id = Environment.GetEnvironmentVariable("DAMI_ACTOR");
        var kindText = Environment.GetEnvironmentVariable("DAMI_ACTOR_KIND");
        var kind = Enum.TryParse<TaskActorKind>(kindText, ignoreCase: true, out var parsed)
            ? parsed
            : TaskActorKind.Human;
        return new TaskActor(
            string.IsNullOrWhiteSpace(id) ? Environment.UserName.ToLowerInvariant() : id.Trim(),
            kind);
    }
}
