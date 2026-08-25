using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dami.Authentication;

/// <summary>Isolated identity and OpenIddict persistence in the dami_auth schema.</summary>
public sealed class DamiAuthDbContext(
    DbContextOptions<DamiAuthDbContext> options)
    : IdentityDbContext<DamiIdentity, IdentityRole<Guid>, Guid>(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasDefaultSchema("dami_auth");
        base.OnModelCreating(builder);
    }
}
