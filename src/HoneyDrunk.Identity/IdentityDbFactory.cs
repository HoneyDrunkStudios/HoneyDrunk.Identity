using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HoneyDrunk.Identity;

/// <summary>Creates the SQL context for migration tooling.</summary>
public sealed class IdentityDbFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <inheritdoc />
    public IdentityDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__identity") ?? "Server=(localdb)\\PocketQuests;Database=HoneyDrunkIdentity;Integrated Security=true;Encrypt=true;TrustServerCertificate=true").Options);
}
