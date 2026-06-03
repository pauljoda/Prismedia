using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static class EntityRelationshipModelConfiguration {
    public static void ConfigureEntityRelationshipModel(this ModelBuilder modelBuilder) {
        modelBuilder.Entity<EntityRelationshipLinkRow>(entity => {
            entity.ToTable("entity_relationship_links");
            entity.HasKey(row => new { row.EntityId, row.RelationshipCode, row.TargetEntityId });
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.RelationshipCode).HasColumnName("relationship_code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Label).HasColumnName("label").HasMaxLength(128).IsRequired();
            entity.Property(row => row.TargetEntityId).HasColumnName("target_entity_id");
            entity.Property(row => row.TargetKindCode).HasColumnName("target_kind_code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.SortOrder).HasColumnName("sort_order");
            entity.Property(row => row.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => new { row.EntityId, row.RelationshipCode, row.SortOrder });
            entity.HasIndex(row => new { row.TargetKindCode, row.TargetEntityId });
            // Backs the live reference-count rollup: filter by target_entity_id, count distinct
            // source entity_id grouped by source kind. Leading with target_entity_id and covering
            // entity_id keeps the aggregate index-only.
            entity.HasIndex(row => new { row.TargetEntityId, row.EntityId });
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.TargetEntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityKindRow>()
                .WithMany()
                .HasForeignKey(row => row.TargetKindCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EntityUrlRow>(entity => {
            entity.ToTable("entity_urls");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Url).HasColumnName("url").IsRequired();
            entity.Property(row => row.Label).HasColumnName("label").HasMaxLength(128);
            entity.Property(row => row.SortOrder).HasColumnName("sort_order");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => new { row.EntityId, row.Url }).IsUnique();
            entity.HasIndex(row => new { row.EntityId, row.SortOrder });
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityExternalIdRow>(entity => {
            entity.ToTable("entity_external_ids");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Provider).HasColumnName("provider").HasMaxLength(128).IsRequired();
            entity.Property(row => row.Value).HasColumnName("value").IsRequired();
            entity.Property(row => row.Url).HasColumnName("url");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.EntityId, row.Provider }).IsUnique();
            entity.HasIndex(row => row.Provider);
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
