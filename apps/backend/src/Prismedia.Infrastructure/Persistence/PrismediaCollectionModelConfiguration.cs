using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureCollections(ModelBuilder modelBuilder) {
        modelBuilder.Entity<CollectionDetailRow>(entity => {
            entity.ToTable("collection_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Mode)
                .HasColumnName("mode")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<CollectionMode>());
            entity.Property(row => row.RuleTreeJson).HasColumnName("rule_tree_json").HasColumnType("jsonb");
            entity.Property(row => row.CoverMode)
                .HasColumnName("cover_mode")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<CollectionCoverMode>());
            entity.Property(row => row.CoverItemEntityId).HasColumnName("cover_item_entity_id");
            entity.Property(row => row.SlideshowDurationSeconds).HasColumnName("slideshow_duration_seconds");
            entity.Property(row => row.SlideshowAutoAdvance).HasColumnName("slideshow_auto_advance");
            entity.Property(row => row.LastRefreshedAt).HasColumnName("last_refreshed_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<CollectionDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionItemDetailRow>(entity => {
            entity.ToTable("collection_item_details");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.CollectionEntityId).HasColumnName("collection_entity_id");
            entity.Property(row => row.ItemEntityId).HasColumnName("item_entity_id");
            entity.Property(row => row.Source)
                .HasColumnName("source")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<CollectionItemSource>());
            entity.Property(row => row.SortOrder).HasColumnName("sort_order");
            entity.Property(row => row.AddedAt).HasColumnName("added_at");
            entity.HasIndex(row => new { row.CollectionEntityId, row.ItemEntityId }).IsUnique();
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.CollectionEntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.ItemEntityId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
