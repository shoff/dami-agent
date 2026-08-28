using System.Security.Claims;
using Dami.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Dami.Host;

/// <summary>User interaction required by the maintained OIDC server flows.</summary>
public static class AuthenticationEndpoints
{
    /// <summary>Maps interactive identity verification endpoints.</summary>
    public static void Map(WebApplication app)
    {
        app.MapPost("/connect/authorize", AuthorizeAsync).AllowAnonymous();
        app.MapPost("/connect/verify", VerifyDeviceAsync).AllowAnonymous();
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        UserManager<DamiIdentity> users,
        CancellationToken cancellationToken)
    {
        ClaimsPrincipal? principal = await AuthenticateUserAsync(
            context, users, cancellationToken).ConfigureAwait(false);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        OpenIddictRequest request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The authorization request is unavailable.");
        principal.SetScopes(request.GetScopes());
        return Results.SignIn(principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> VerifyDeviceAsync(
        HttpContext context,
        UserManager<DamiIdentity> users,
        CancellationToken cancellationToken)
    {
        ClaimsPrincipal? principal = await AuthenticateUserAsync(
            context, users, cancellationToken).ConfigureAwait(false);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        AuthenticateResult authorization = await context.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
        principal.SetScopes(authorization.Principal?.GetScopes() ?? []);
        return Results.SignIn(principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<ClaimsPrincipal?> AuthenticateUserAsync(
        HttpContext context,
        UserManager<DamiIdentity> users,
        CancellationToken cancellationToken)
    {
        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken)
            .ConfigureAwait(false);
        DamiIdentity? user = await users.FindByNameAsync(form["username"]!)
            .ConfigureAwait(false);
        if (user is null || !await users.CheckPasswordAsync(user, form["password"]!)
            .ConfigureAwait(false))
        {
            return null;
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString("D"));
        identity.SetClaim(OpenIddictConstants.Claims.Name, user.UserName);
        identity.SetClaim(DamiAuthorizationClaims.ACTOR_KIND, "Human");
        identity.SetDestinations(claim =>
            [OpenIddictConstants.Destinations.AccessToken]);
        return new ClaimsPrincipal(identity);
    }
}
