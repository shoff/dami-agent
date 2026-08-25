using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class DamiAuthDbContextTests
{
    [Fact]
    public void Model_Should_Isolate_Identity_And_OpenIddict_State_In_Auth_Schema()
    {
        using DamiAuthDbContext context = CreateContext();

        string?[] schemas = context.Model.GetEntityTypes()
            .Select(entity => entity.GetSchema()).Distinct().ToArray();

        Assert.Equal("dami_auth", Assert.Single(schemas));
        Assert.Contains(
            context.Model.GetEntityTypes(), entity => entity.ClrType == typeof(DamiIdentity));
        Assert.Contains(context.Model.GetEntityTypes(), entity =>
            entity.ClrType.Name.StartsWith("OpenIddictEntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Model_Should_Have_A_Checked_In_Foundation_Migration()
    {
        using DamiAuthDbContext context = CreateContext();

        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("_AuthFoundation", StringComparison.Ordinal));
    }

    private static DamiAuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DamiAuthDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused")
            .UseOpenIddict()
            .Options;
        return new DamiAuthDbContext(options);
    }
}
