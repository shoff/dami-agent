using OpenIddict.Abstractions;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class DamiClientProfilesTests
{
    [Fact]
    public void Profiles_Should_Bind_Each_Client_To_Its_Required_Flow_And_Scopes()
    {
        OpenIddictApplicationDescriptor cli = DamiClientProfiles.Cli();
        OpenIddictApplicationDescriptor gui = DamiClientProfiles.Gui(
            new Uri("http://127.0.0.1:5812/callback"));
        OpenIddictApplicationDescriptor service = DamiClientProfiles.Service(
            "dami-reflection", "Reflection service", [DamiAuthorizationScopes.RUNTIME_READ]);

        Assert.Equal(OpenIddictConstants.ClientTypes.Public, cli.ClientType);
        Assert.Contains(OpenIddictConstants.Permissions.GrantTypes.DeviceCode, cli.Permissions);
        Assert.Contains(Scope(DamiAuthorizationScopes.APPROVALS_RESOLVE), cli.Permissions);
        Assert.Equal(OpenIddictConstants.ClientTypes.Public, gui.ClientType);
        Assert.Contains(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode, gui.Permissions);
        Assert.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange, gui.Requirements);
        Assert.Equal(OpenIddictConstants.ClientTypes.Confidential, service.ClientType);
        Assert.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials, service.Permissions);
        Assert.DoesNotContain(Scope(DamiAuthorizationScopes.APPROVALS_RESOLVE), service.Permissions);
        Assert.Throws<ArgumentException>(() => DamiClientProfiles.Service(
            "overpowered", "Overpowered service", [DamiAuthorizationScopes.APPROVALS_RESOLVE]));
    }

    private static string Scope(string value) =>
        OpenIddictConstants.Permissions.Prefixes.Scope + value;
}
