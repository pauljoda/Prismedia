using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureEntityPageManifests(ModelBuilder modelBuilder) {
        modelBuilder.Entity<EntityPageManifestRow>(entity => {
            entity.ToTable("entity_page_manifests", table =>
                table.HasCheckConstraint(
                    "ck_entity_page_manifests_cover_ordinal",
                    "cover_ordinal IS NULL OR cover_ordinal >= 0"));
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Direction)
                .HasColumnName("direction")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<PageReadingDirection>())
                .IsRequired();
            entity.Property(row => row.DefaultMode)
                .HasColumnName("default_mode")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<ReaderMode>())
                .IsRequired();
            entity.Property(row => row.CoverOrdinal).HasColumnName("cover_ordinal");
            entity.Property(row => row.SourceSignature)
                .HasColumnName("source_signature")
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<EntityPageManifestRow>(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityPageEntryRow>(entity => {
            entity.ToTable("entity_page_entries", table => {
                table.HasCheckConstraint("ck_entity_page_entries_ordinal", "ordinal >= 0");
                table.HasCheckConstraint(
                    "ck_entity_page_entries_dimensions",
                    "(width IS NULL OR width >= 0) AND (height IS NULL OR height >= 0)");
            });
            entity.HasKey(row => new { row.EntityId, row.Ordinal });
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Ordinal).HasColumnName("ordinal");
            entity.Property(row => row.ArchiveMember)
                .HasColumnName("archive_member")
                .HasMaxLength(2048)
                .IsRequired();
            entity.Property(row => row.MimeType)
                .HasColumnName("mime_type")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(row => row.Width).HasColumnName("width");
            entity.Property(row => row.Height).HasColumnName("height");
            entity.Property(row => row.PageType)
                .HasColumnName("page_type")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<PageType>())
                .IsRequired();
            entity.Property(row => row.IsDoublePage).HasColumnName("is_double_page");
            entity.Property(row => row.Checksum).HasColumnName("checksum").HasMaxLength(128);
            entity.HasIndex(row => new { row.EntityId, row.ArchiveMember }).IsUnique();
            entity.HasOne<EntityPageManifestRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
