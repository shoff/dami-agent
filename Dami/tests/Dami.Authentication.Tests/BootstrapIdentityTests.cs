using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class BootstrapIdentityTests
{
    private const string CONNECTION =
        "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_app;Passfile=/home/steve/.pgpass";

    private const string PASSWORD = "Test-only-password-42!";

    [Fact]
    public async Task Should_Create_The_Bootstrap_Identity_On_A_Fresh_Host()
    {
        // The gap this closes mirrors the client one: the endpoints authenticate against
        // UserManager, the clients are registered, and no code path ever created a user —
        // so with the flag on, every login on every client would fail at the password check.
        var username = $"bootstrap-{Guid.NewGuid():N}";
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        try
        {
            var outcome = await new DamiIdentityProvisioner(users)
                .EnsureIdentityAsync(username, PASSWORD, CancellationToken.None);

            Assert.Equal(IdentityProvisionResult.Created, outcome.Result);
        }
        finally
        {
            await DeleteAsync(users, username);
        }
    }

    [Fact]
    public async Task Should_Leave_An_Existing_Identity_And_Its_Password_Alone()
    {
        // A restarting host runs the seeder again. Re-provisioning must not quietly reset
        // the password to whatever the bootstrap configuration still holds.
        var username = $"bootstrap-{Guid.NewGuid():N}";
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        var provisioner = new DamiIdentityProvisioner(users);
        try
        {
            await provisioner.EnsureIdentityAsync(username, PASSWORD, CancellationToken.None);
            var again = await provisioner.EnsureIdentityAsync(
                username, "A-different-password-43!", CancellationToken.None);

            var user = await users.FindByNameAsync(username);
            Assert.Equal(
                (IdentityProvisionResult.AlreadyExists, true),
                (again.Result, await users.CheckPasswordAsync(user!, PASSWORD)));
        }
        finally
        {
            await DeleteAsync(users, username);
        }
    }

    [Fact]
    public async Task Should_Report_A_Password_The_Policy_Refuses()
    {
        var username = $"bootstrap-{Guid.NewGuid():N}";
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        try
        {
            var outcome = await new DamiIdentityProvisioner(users)
                .EnsureIdentityAsync(username, "x", CancellationToken.None);

            Assert.Equal(IdentityProvisionResult.Failed, outcome.Result);
        }
        finally
        {
            await DeleteAsync(users, username);
        }
    }

    [Fact]
    public async Task Should_Say_Why_A_Refused_Password_Was_Refused()
    {
        var username = $"bootstrap-{Guid.NewGuid():N}";
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        try
        {
            var outcome = await new DamiIdentityProvisioner(users)
                .EnsureIdentityAsync(username, "x", CancellationToken.None);

            Assert.NotNull(outcome.Error);
        }
        finally
        {
            await DeleteAsync(users, username);
        }
    }

    private static async Task DeleteAsync(UserManager<DamiIdentity> users, string username)
    {
        var user = await users.FindByNameAsync(username);
        if (user is not null)
        {
            await users.DeleteAsync(user);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DamiAuthDbContext>(options =>
        {
            options.UseNpgsql(CONNECTION);
            options.UseOpenIddict();
        });
        services.AddIdentityCore<DamiIdentity>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<DamiAuthDbContext>();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
