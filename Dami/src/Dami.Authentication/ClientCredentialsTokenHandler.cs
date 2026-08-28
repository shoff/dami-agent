using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Dami.Authentication;

/// <summary>Creates service principals after OpenIddict validates client credentials.</summary>
public sealed class ClientCredentialsTokenHandler : IOpenIddictServerHandler<HandleTokenRequestContext>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(HandleTokenRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Request.IsClientCredentialsGrantType())
        {
            return ValueTask.CompletedTask;
        }

        var identity = new ClaimsIdentity(
            authenticationType: "DamiClientCredentials",
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, context.Request.ClientId);
        identity.SetClaim(DamiAuthorizationClaims.ACTOR_KIND, "Agent");
        identity.SetScopes(context.Request.GetScopes());
        identity.SetDestinations(claim => claim.Type == DamiAuthorizationClaims.ACTOR_KIND
            ? [OpenIddictConstants.Destinations.AccessToken]
            : []);
        context.SignIn(new ClaimsPrincipal(identity));
        return ValueTask.CompletedTask;
    }
}
