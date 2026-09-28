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
    protected override void ApplyConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRow>().HasKey(x => x.UserId);
        modelBuilder.Entity<UserRow>().Property(x => x.UserId).HasMaxLength(30).IsUnicode(false);
        modelBuilder.Entity<UserRow>().Property(x => x.State).HasMaxLength(20);
        modelBuilder.Entity<SubjectRow>().HasKey(x => x.SubjectKey);
        modelBuilder.Entity<SubjectRow>().Property(x => x.SubjectKey).HasMaxLength(64).IsUnicode(false);
        modelBuilder.Entity<SubjectRow>().HasOne<UserRow>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IdentityAuditRow>().HasKey(x => x.Id);
        modelBuilder.Entity<IdentityAuditRow>().ToTable("Audit");
        modelBuilder.Entity<IdentityAuditRow>().Property(x => x.EventName).HasMaxLength(100);
        modelBuilder.Entity<IdentityAuditRow>().Property(x => x.Outcome).HasMaxLength(40);
        modelBuilder.Entity<IdentityAuditRow>().Property(x => x.ActorHash).HasMaxLength(64).IsUnicode(false);
        modelBuilder.Entity<AuditRecord>().ToTable("AuditRecords");
        modelBuilder.Entity<AuditRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<AuditRecord>().Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        modelBuilder.Entity<AuditRecord>().Property(x => x.EventName).HasMaxLength(200);
        modelBuilder.Entity<AuditRecord>().Property(x => x.TenantId).HasMaxLength(100);
        modelBuilder.Entity<AuditRecord>().HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
