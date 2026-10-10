using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Identity.Persistence.Configurations;

/// <summary>Fluent mapping for the Erasures table.</summary>
public sealed class ErasureMarkerEntityConfiguration : IEntityTypeConfiguration<ErasureMarkerEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ErasureMarkerEntity> builder)
    {
        builder.ToTable("Erasures");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).HasMaxLength(30).IsUnicode(false);
    }
}
