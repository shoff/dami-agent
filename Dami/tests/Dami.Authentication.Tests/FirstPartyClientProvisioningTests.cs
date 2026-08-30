using NSubstitute;
using OpenIddict.Abstractions;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class FirstPartyClientProvisioningTests
{
    private static readonly Uri redirect = new("http://127.0.0.1:5899/connect/callback");

    private static IOpenIddictApplicationManager Manager(params string[] alreadyRegistered)
    {
        var manager = Substitute.For<IOpenIddictApplicationManager>();
        manager.FindByClientIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult<object?>(
                alreadyRegistered.Contains((string)call[0]) ? new object() : null));
        return manager;
    }

    [Fact]
    public async Task Should_Register_Both_First_Party_Clients_On_A_Fresh_Host()
    {
        // The gap this closes: the profiles, endpoints and policies were all built and
        // green, but nothing ever created these registrations outside a test fixture, so
        // every flow would have failed at the first request with an unknown client.
        var manager = Manager();

        var created = await new DamiClientProvisioner(manager)
            .EnsureFirstPartyClientsAsync(redirect, CancellationToken.None);

        Assert.Equal(["dami-cli", "dami-gui"], created);
    }

    [Fact]
    public async Task Should_Create_Nothing_When_Both_Already_Exist()
    {
        // A host restarts often. A second registration for the same client id is a
        // conflict, not a no-op.
        var manager = Manager("dami-cli", "dami-gui");

        var created = await new DamiClientProvisioner(manager)
            .EnsureFirstPartyClientsAsync(redirect, CancellationToken.None);

        Assert.Empty(created);
        await manager.DidNotReceive().CreateAsync(
            Arg.Any<OpenIddictApplicationDescriptor>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Add_Only_The_Missing_Client()
    {
        var manager = Manager("dami-cli");

        var created = await new DamiClientProvisioner(manager)
            .EnsureFirstPartyClientsAsync(redirect, CancellationToken.None);

        Assert.Equal(["dami-gui"], created);
    }

    [Fact]
    public async Task Should_Give_The_Desktop_Client_The_Registered_Redirect()
    {
        // A PKCE redirect must be registered ahead of time; one that does not match what
        // the desktop client listens on fails at the last step of the flow.
        var manager = Manager();
        OpenIddictApplicationDescriptor? gui = null;
        await manager.CreateAsync(
            Arg.Do<OpenIddictApplicationDescriptor>(d => gui = d.ClientId == "dami-gui" ? d : gui),
            Arg.Any<CancellationToken>());

        await new DamiClientProvisioner(manager).EnsureFirstPartyClientsAsync(
            redirect, CancellationToken.None);

        Assert.NotNull(gui);
        Assert.Contains(redirect, gui.RedirectUris);
    }

    [Fact]
    public async Task Should_Not_Give_A_Public_Client_A_Secret()
    {
        // Neither of these can keep a secret on a machine its user controls. Storing one
        // would be theatre, and the device and PKCE flows exist precisely so it is not
        // needed.
        var manager = Manager();
        var descriptors = new List<OpenIddictApplicationDescriptor>();
        await manager.CreateAsync(
            Arg.Do<OpenIddictApplicationDescriptor>(descriptors.Add), Arg.Any<CancellationToken>());

        await new DamiClientProvisioner(manager).EnsureFirstPartyClientsAsync(
            redirect, CancellationToken.None);

        Assert.All(descriptors, descriptor => Assert.Null(descriptor.ClientSecret));
    }
}
