using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static class BaseEntityModelConfiguration {
    public static void ConfigureBaseEntityModel(this ModelBuilder modelBuilder) {
        modelBuilder.Entity<EntityKindRow>(entity => {
            entity.ToTable("entity_kinds");
            entity.HasKey(row => row.Code);
            entity.Property(row => row.Code).HasColumnName("code").HasMaxLength(64);
            entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
            entity.Property(row => row.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
            entity.Property(row => row.StorageShape).HasColumnName("storage_shape").HasMaxLength(64).IsRequired();
            entity.HasData(EntityKindRegistry.All.Select(kind => new EntityKindRow {
                Code = kind.Code,
                DisplayName = kind.DisplayName,
                Category = kind.Category.ToString(),
                StorageShape = kind.StorageShape.ToCode()
            }));
        });

        modelBuilder.Entity<EntityRow>(entity => {
            entity.ToTable("entities", table => {
                table.HasCheckConstraint(
                    "ck_entities_rating",
                    "rating_value IS NULL OR (rating_value >= 0 AND rating_value <= 5)");
                table.HasCheckConstraint(
                    "ck_entities_lifecycle_claim",
                    "(lifecycle_claim_kind IS NULL AND lifecycle_claim_id IS NULL AND lifecycle_claimed_at IS NULL) OR "
                    + "(lifecycle_claim_kind IS NOT NULL AND lifecycle_claim_id IS NOT NULL AND lifecycle_claimed_at IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.KindCode).HasColumnName("kind_code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
            entity.Property(row => row.SortName)
                .HasColumnName("sort_title")
                .HasComputedColumnSql(
                    """
                    trim(CASE
                        WHEN lower(title) LIKE 'the %' THEN substr(title, 5)
                        WHEN lower(title) LIKE 'an %' THEN substr(title, 4)
                        WHEN lower(title) LIKE 'a %' THEN substr(title, 3)
                        ELSE title
                    END)
                    """,
                    stored: true);
            entity.Property(row => row.ParentEntityId).HasColumnName("parent_entity_id");
            entity.Property(row => row.SortOrder).HasColumnName("sort_order");
            entity.Property(row => row.IsNsfw).HasColumnName("is_nsfw").HasDefaultValue(false);
            entity.Property(row => row.IsOrganized).HasColumnName("is_organized").HasDefaultValue(false);
            entity.Property(row => row.IsWanted).HasColumnName("is_wanted").HasDefaultValue(false);
            entity.Property(row => row.LifecycleClaimKind)
                .HasColumnName("lifecycle_claim_kind")
                .HasMaxLength(64)
                .HasConversion(
                    value => value == null ? null : value.Value.ToCode(),
                    value => value == null ? null : value.DecodeAs<EntityLifecycleClaimKind>());
            entity.Property(row => row.LifecycleClaimId).HasColumnName("lifecycle_claim_id");
            entity.Property(row => row.LifecycleClaimedAt).HasColumnName("lifecycle_claimed_at");
            entity.Property(row => row.AutoIdentifyAttempts).HasColumnName("auto_identify_attempts").HasDefaultValue(0);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.KindCode, row.Title });
            entity.HasIndex(row => new { row.KindCode, row.SortName });
            entity.HasIndex(row => row.ParentEntityId);
            entity.HasOne<EntityKindRow>()
                .WithMany()
                .HasForeignKey(row => row.KindCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.ParentEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

    }
}
