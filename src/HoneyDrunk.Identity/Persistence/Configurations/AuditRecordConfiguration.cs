using HoneyDrunk.Audit.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Identity.Persistence.Configurations;

/// <summary>Fluent mapping for the AuditRecords table.</summary>
public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        builder.Property(x => x.EventName).HasMaxLength(200);
        builder.Property(x => x.TenantId).HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
