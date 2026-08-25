using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dami.Authentication;

/// <summary>Creates the isolated auth model for checked-in EF migration generation.</summary>
public sealed class DamiAuthDbContextFactory : IDesignTimeDbContextFactory<DamiAuthDbContext>
{
    /// <inheritdoc />
    public DamiAuthDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DamiAuthDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Database=dami-data;Username=dami_ddl",
                postgres => postgres.MigrationsHistoryTable(
                    "__EFMigrationsHistory", "dami_auth"))
            .UseOpenIddict()
            .Options;
        return new DamiAuthDbContext(options);
    }
}
