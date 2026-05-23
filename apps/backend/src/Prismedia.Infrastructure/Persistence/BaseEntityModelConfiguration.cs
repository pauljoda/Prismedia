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
            entity.ToTable("entities");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.KindCode).HasColumnName("kind_code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
            entity.Property(row => row.ParentEntityId).HasColumnName("parent_entity_id");
            entity.Property(row => row.SortOrder).HasColumnName("sort_order");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.DeletedAt).HasColumnName("deleted_at");
            entity.HasIndex(row => new { row.KindCode, row.Title });
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

        modelBuilder.Entity<EntityRatingRow>(entity => {
            entity.ToTable("entity_ratings");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Value).HasColumnName("value");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<EntityRatingRow>(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint(
                "ck_entity_ratings_value",
                "value >= 0 AND value <= 5"));
        });

        modelBuilder.Entity<EntityFlagRow>(entity => {
            entity.ToTable("entity_flags");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.IsFavorite).HasColumnName("is_favorite");
            entity.Property(row => row.IsNsfw).HasColumnName("is_nsfw");
            entity.Property(row => row.IsOrganized).HasColumnName("is_organized");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<EntityFlagRow>(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
