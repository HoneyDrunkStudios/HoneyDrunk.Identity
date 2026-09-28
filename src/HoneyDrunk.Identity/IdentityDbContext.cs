using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.EntityFramework.Context;
using Microsoft.EntityFrameworkCore;

namespace HoneyDrunk.Identity;

/// <summary>Stores user mappings and canonical audit envelopes through HoneyDrunk.Data.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : HoneyDrunkDbContext(options)
{
    /// <summary>Gets the stable user directory.</summary>
    public DbSet<UserRow> Users => Set<UserRow>();

    /// <summary>Gets verified issuer-subject mappings.</summary>
    public DbSet<SubjectRow> Subjects => Set<SubjectRow>();

    // Keep the original records intact; all new writes use the canonical envelope.

    /// <summary>Gets historical prototype audit rows preserved during migration.</summary>
    public DbSet<IdentityAuditRow> LegacyAudit => Set<IdentityAuditRow>();

    /// <summary>Gets canonical append-only audit records.</summary>
    public DbSet<AuditRecord> Audit => Set<AuditRecord>();

    /// <inheritdoc />
    protected override void ApplyConfigurations(ModelBuilder b)
    {
        b.Entity<UserRow>().HasKey(x => x.UserId);
        b.Entity<UserRow>().Property(x => x.UserId).HasMaxLength(30).IsUnicode(false);
        b.Entity<UserRow>().Property(x => x.State).HasMaxLength(20);
        b.Entity<SubjectRow>().HasKey(x => x.SubjectKey);
        b.Entity<SubjectRow>().Property(x => x.SubjectKey).HasMaxLength(64).IsUnicode(false);
        b.Entity<SubjectRow>().HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<IdentityAuditRow>().HasKey(x => x.Id);
        b.Entity<IdentityAuditRow>().ToTable("Audit");
        b.Entity<IdentityAuditRow>().Property(x => x.EventName).HasMaxLength(100);
        b.Entity<IdentityAuditRow>().Property(x => x.Outcome).HasMaxLength(40);
        b.Entity<IdentityAuditRow>().Property(x => x.ActorHash).HasMaxLength(64).IsUnicode(false);
        b.Entity<AuditRecord>().ToTable("AuditRecords");
        b.Entity<AuditRecord>().HasKey(x => x.Id);
        b.Entity<AuditRecord>().Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        b.Entity<AuditRecord>().Property(x => x.EventName).HasMaxLength(200);
        b.Entity<AuditRecord>().Property(x => x.TenantId).HasMaxLength(100);
        b.Entity<AuditRecord>().HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
