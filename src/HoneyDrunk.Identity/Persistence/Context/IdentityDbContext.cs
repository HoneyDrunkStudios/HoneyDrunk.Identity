using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.EntityFramework.Context;
using HoneyDrunk.Data.Outbox;
using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity.Persistence.Context;

/// <summary>Stores user mappings and canonical audit envelopes through HoneyDrunk.Data.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : HoneyDrunkDbContext(options)
{
    /// <summary>Gets the stable user directory.</summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>Gets verified issuer-subject mappings.</summary>
    public DbSet<SubjectEntity> Subjects => Set<SubjectEntity>();

    /// <summary>Gets pending downstream lifecycle acknowledgments.</summary>
    public DbSet<LifecycleDeliveryEntity> Deliveries => Set<LifecycleDeliveryEntity>();

    /// <summary>Gets minimal verified-erasure markers used by restore barriers.</summary>
    public DbSet<ErasureMarkerEntity> Erasures => Set<ErasureMarkerEntity>();

    // Keep the original records intact; all new writes use the canonical envelope.

    /// <summary>Gets historical prototype audit rows preserved during migration.</summary>
    public DbSet<IdentityAuditEntity> LegacyAudit => Set<IdentityAuditEntity>();

    /// <summary>Gets canonical append-only audit records.</summary>
    public DbSet<AuditRecord> Audit => Set<AuditRecord>();

    /// <inheritdoc />
    protected override void ApplyConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutboxConfiguration();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
