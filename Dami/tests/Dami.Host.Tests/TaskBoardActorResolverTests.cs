using System.Security.Claims;
using Dami.Authentication;
using Dami.Contracts.TaskBoard;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Xunit;

namespace Dami.Host.Tests;

public sealed class TaskBoardActorResolverTests
{
    [Fact]
    public void Resolve_Should_Preserve_Submitted_Actor_When_Authentication_Is_Disabled()
    {
        var resolver = new TaskBoardActorResolver(Options.Create(
            new DamiAuthenticationOptions { Enabled = false }));
        var submitted = new TaskActor("codex", TaskActorKind.Agent);

        TaskActor? actor = resolver.Resolve(new ClaimsPrincipal(), submitted);

        Assert.Equal(submitted, actor);
    }

    [Fact]
    public void Resolve_Should_Use_Validated_Claims_When_Authentication_Is_Enabled()
    {
        var resolver = new TaskBoardActorResolver(Options.Create(
            new DamiAuthenticationOptions { Enabled = true }));
        var identity = new ClaimsIdentity(
        [
            new Claim(OpenIddictConstants.Claims.Subject, "identity-42"),
            new Claim(DamiAuthorizationClaims.ACTOR_KIND, "Human"),
        ], "test");
        var submitted = new TaskActor("spoofed-codex", TaskActorKind.Agent);

        TaskActor? actor = resolver.Resolve(new ClaimsPrincipal(identity), submitted);

        Assert.Equal(new TaskActor("identity-42", TaskActorKind.Human), actor);
    }
}
