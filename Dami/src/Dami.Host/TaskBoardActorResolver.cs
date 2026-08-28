using System.Security.Claims;
using Dami.Authentication;
using Dami.Contracts.TaskBoard;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Dami.Host;

/// <summary>Resolves durable board attribution at the authenticated Host boundary.</summary>
public sealed class TaskBoardActorResolver(IOptions<DamiAuthenticationOptions> options)
{
    private readonly bool authenticationEnabled = options.Value.Enabled;

    /// <summary>Whether actor attribution must come from authenticated claims.</summary>
    public bool UsesAuthenticatedClaims => this.authenticationEnabled;

    /// <summary>Returns the trusted actor, or null when required claims are invalid.</summary>
    public TaskActor? Resolve(ClaimsPrincipal principal, TaskActor submitted) =>
        this.Resolve(principal, submitted.ActorId, submitted.Kind);

    /// <summary>Returns claims-based attribution or validates compatibility input.</summary>
    public TaskActor? Resolve(
        ClaimsPrincipal principal,
        string? submittedActorId,
        TaskActorKind submittedActorKind)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!this.authenticationEnabled)
        {
            return !string.IsNullOrWhiteSpace(submittedActorId)
                && Enum.IsDefined(submittedActorKind)
                    ? new TaskActor(submittedActorId, submittedActorKind)
                    : null;
        }

        var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        var kind = principal.FindFirst(DamiAuthorizationClaims.ACTOR_KIND)?.Value;
        return principal.Identity?.IsAuthenticated == true
            && !string.IsNullOrWhiteSpace(subject)
            && Enum.TryParse(kind, ignoreCase: false, out TaskActorKind actorKind)
            && Enum.IsDefined(actorKind)
                ? new TaskActor(subject, actorKind)
                : null;
    }
}
