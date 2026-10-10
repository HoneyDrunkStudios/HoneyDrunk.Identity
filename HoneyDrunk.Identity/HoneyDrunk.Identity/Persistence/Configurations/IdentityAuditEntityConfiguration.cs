using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Identity.Persistence.Configurations;

/// <summary>Fluent mapping for the Audit table.</summary>
public sealed class IdentityAuditEntityConfiguration : IEntityTypeConfiguration<IdentityAuditEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdentityAuditEntity> builder)
    {
        builder.ToTable("Audit");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventName).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(40);
        builder.Property(x => x.ActorHash).HasMaxLength(64).IsUnicode(false);
    }
}
