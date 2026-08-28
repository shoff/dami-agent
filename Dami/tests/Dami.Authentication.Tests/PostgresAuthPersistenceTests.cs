using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIddict.Abstractions;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class PostgresAuthPersistenceTests
{
    private const string CONNECTION =
        "Host=127.0.0.1;Port=5432;Database=dami-data;Username=dami_app;Passfile=/home/steve/.pgpass";

    [Fact]
    public async Task Service_Enrollment_Should_Return_A_Once_Valid_Least_Privilege_Secret()
    {
        string clientId = $"service-{Guid.NewGuid():N}";
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var provisioner = new DamiClientProvisioner(applications);
        object? application = null;
        try
        {
            string secret = await provisioner.EnrollServiceAsync(
                clientId, "Test service", [DamiAuthorizationScopes.RUNTIME_READ],
                CancellationToken.None);
            application = await applications.FindByClientIdAsync(clientId, CancellationToken.None);
            var descriptor = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(descriptor, application!, CancellationToken.None);

            Assert.True(await applications.ValidateClientSecretAsync(
                application!, secret, CancellationToken.None));
            Assert.Contains(Scope(DamiAuthorizationScopes.RUNTIME_READ), descriptor.Permissions);
            Assert.DoesNotContain(Scope(DamiAuthorizationScopes.RUNTIME_WRITE), descriptor.Permissions);
        }
        finally
        {
            if (application is not null)
            {
                await applications.DeleteAsync(application, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Runtime_Role_Should_Persist_Hashed_Identity_And_Client_Secrets_Async()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string clientId = $"integration-{suffix}";
        const string clientSecret = "not-persisted-in-plaintext";
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<DamiIdentity>>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var user = new DamiIdentity { UserName = $"user-{suffix}" };
        object? application = null;
        bool userCreated = false;
        try
        {
            IdentityResult created = await users.CreateAsync(user, "Test-only-password-42!");
            userCreated = created.Succeeded;
            Assert.True(created.Succeeded);
            application = await CreateAndValidateClientAsync(
                applications, clientId, clientSecret);
        }
        finally
        {
            if (application is not null)
            {
                await applications.DeleteAsync(application, CancellationToken.None);
            }

            if (userCreated)
            {
                Assert.True((await users.DeleteAsync(user)).Succeeded);
            }
        }
    }

    [Fact]
    public async Task Runtime_Role_Should_Not_Access_Migration_History_Async()
    {
        await using var connection = new NpgsqlConnection(CONNECTION);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("""
            select has_table_privilege(
                'dami_app', 'dami_auth."__EFMigrationsHistory"', 'select,insert,update,delete');
            """, connection);

        Assert.False((bool)(await command.ExecuteScalarAsync(CancellationToken.None))!);
    }

    private static async Task AssertSecretIsHashedAsync(string clientId, string attempted)
    {
        await using var connection = new NpgsqlConnection(CONNECTION);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand("""
            select "ClientSecret"
              from dami_auth."OpenIddictApplications"
             where "ClientId" = @client;
            """, connection);
        command.Parameters.AddWithValue("client", clientId);

        Assert.NotEqual(
            attempted,
            (string)(await command.ExecuteScalarAsync(CancellationToken.None))!);
    }

    private static string Scope(string value) =>
        OpenIddictConstants.Permissions.Prefixes.Scope + value;

    private static async Task<object> CreateAndValidateClientAsync(
        IOpenIddictApplicationManager applications,
        string clientId,
        string clientSecret)
    {
        await applications.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            DisplayName = "integration test client",
        }, CancellationToken.None);
        object application = (await applications.FindByClientIdAsync(
            clientId, CancellationToken.None))!;
        Assert.True(await applications.ValidateClientSecretAsync(
            application, clientSecret, CancellationToken.None));
        await AssertSecretIsHashedAsync(clientId, clientSecret);
        return application;
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
        services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore()
            .UseDbContext<DamiAuthDbContext>());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
