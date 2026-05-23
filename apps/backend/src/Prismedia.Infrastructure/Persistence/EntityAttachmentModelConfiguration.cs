using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static class EntityAttachmentModelConfiguration {
    public static void ConfigureEntityAttachmentModel(this ModelBuilder modelBuilder) {
        modelBuilder.Entity<EntityMarkerRow>(entity => {
            entity.ToTable("entity_markers");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Title).HasColumnName("title").IsRequired();
            entity.Property(row => row.Seconds).HasColumnName("seconds");
            entity.Property(row => row.EndSeconds).HasColumnName("end_seconds");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.EntityId, row.Seconds });
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntitySubtitleRow>(entity => {
            entity.ToTable("entity_subtitles");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Language).HasColumnName("language").HasMaxLength(32).IsRequired();
            entity.Property(row => row.Label).HasColumnName("label");
            entity.Property(row => row.Format).HasColumnName("format").HasMaxLength(32).IsRequired();
            entity.Property(row => row.Source)
                .HasColumnName("source")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntitySubtitleSource>())
                .IsRequired();
            entity.Property(row => row.StoragePath).HasColumnName("storage_path").IsRequired();
            entity.Property(row => row.SourceFormat).HasColumnName("source_format").HasMaxLength(32).IsRequired();
            entity.Property(row => row.SourcePath).HasColumnName("source_path");
            entity.Property(row => row.IsDefault).HasColumnName("is_default");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => new { row.EntityId, row.Language, row.Source }).IsUnique();
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityFileRow>(entity => {
            entity.ToTable("entity_files");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Role)
                .HasColumnName("role")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityFileRole>())
                .IsRequired();
            entity.Property(row => row.Path).HasColumnName("path").IsRequired();
            entity.Property(row => row.MimeType).HasColumnName("mime_type").HasMaxLength(128);
            entity.Property(row => row.SizeBytes).HasColumnName("size_bytes");
            entity.Property(row => row.Source).HasColumnName("source").HasMaxLength(16).HasDefaultValue("scan").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.EntityId, row.Role }).IsUnique();
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VideoDetailRow>(entity => {
            entity.ToTable("video_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.Property(row => row.SubtitlesExtractedAt).HasColumnName("subtitles_extracted_at");
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<VideoDetailRow>(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LibraryRootRow>()
                .WithMany()
                .HasForeignKey(row => row.LibraryRootId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
