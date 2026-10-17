using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Identity.Persistence.Configurations;

/// <summary>Fluent mapping for the Deliveries table.</summary>
public sealed class LifecycleDeliveryEntityConfiguration : IEntityTypeConfiguration<LifecycleDeliveryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LifecycleDeliveryEntity> builder)
    {
        builder.ToTable("Deliveries");
        builder.HasKey(x => new { x.UserId, x.Consumer });
        builder.Property(x => x.Consumer).HasMaxLength(100);
        builder.Property(x => x.UserId).HasMaxLength(30).IsUnicode(false);
    }
}
