using OpenIddict.Abstractions;

namespace Dami.Authentication;

/// <summary>Least-privilege OpenIddict registrations for first-party Dami clients.</summary>
public static class DamiClientProfiles
{
    /// <summary>Creates the public CLI device-flow profile.</summary>
    public static OpenIddictApplicationDescriptor Cli() => new()
    {
        ClientId = "dami-cli",
        ClientType = OpenIddictConstants.ClientTypes.Public,
        ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
        DisplayName = "Dami CLI",
        Permissions =
        {
            OpenIddictConstants.Permissions.Endpoints.DeviceAuthorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.DeviceCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            Scope(DamiAuthorizationScopes.RUNTIME_READ),
            Scope(DamiAuthorizationScopes.RUNTIME_WRITE),
            Scope(DamiAuthorizationScopes.APPROVALS_RESOLVE),
        },
    };

    /// <summary>Creates the public GUI authorization-code/PKCE profile.</summary>
    public static OpenIddictApplicationDescriptor Gui(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "dami-gui",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
            DisplayName = "Dami Desktop",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                Scope(DamiAuthorizationScopes.RUNTIME_READ),
                Scope(DamiAuthorizationScopes.RUNTIME_WRITE),
                Scope(DamiAuthorizationScopes.APPROVALS_RESOLVE),
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange,
            },
        };
        descriptor.RedirectUris.Add(redirectUri);
        return descriptor;
    }

    /// <summary>Creates a confidential client-credentials profile for one service.</summary>
    public static OpenIddictApplicationDescriptor Service(
        string clientId,
        string displayName,
        IReadOnlyCollection<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0 || scopes.Any(scope => scope is not (
                DamiAuthorizationScopes.RUNTIME_READ or DamiAuthorizationScopes.RUNTIME_WRITE)))
        {
            throw new ArgumentException("Services require one or more runtime read/write scopes.", nameof(scopes));
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Systematic,
            DisplayName = displayName,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            },
        };
        foreach (var scope in scopes)
        {
            descriptor.Permissions.Add(Scope(scope));
        }

        return descriptor;
    }

    private static string Scope(string value) =>
        OpenIddictConstants.Permissions.Prefixes.Scope + value;
}
