using HoneyDrunk.Identity.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HoneyDrunk.Identity.Persistence.Configurations;

/// <summary>Fluent mapping for the Subjects table.</summary>
public sealed class SubjectEntityConfiguration : IEntityTypeConfiguration<SubjectEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SubjectEntity> builder)
    {
        builder.ToTable("Subjects");
        builder.HasKey(x => x.SubjectKey);
        builder.Property(x => x.SubjectKey).HasMaxLength(64).IsUnicode(false);
        builder.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
