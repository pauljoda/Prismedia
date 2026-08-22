using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureMediaDetails(ModelBuilder modelBuilder) {
        modelBuilder.Entity<VideoSeriesDetailRow>(entity => {
            entity.ToTable("video_series_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Status).HasColumnName("status");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<VideoSeriesDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GalleryDetailRow>(entity => {
            entity.ToTable("gallery_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.GalleryType)
                .HasColumnName("gallery_type")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<GalleryType>());
            entity.Property(row => row.CoverImageEntityId).HasColumnName("cover_image_entity_id");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<GalleryDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureBooks(modelBuilder);
        ConfigureComics(modelBuilder);
        ConfigureAudio(modelBuilder);
    }

    private static void ConfigureComics(ModelBuilder modelBuilder) {
        modelBuilder.Entity<ComicSeriesDetailRow>(entity => {
            entity.ToTable("comic_series_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Status).HasColumnName("status").HasMaxLength(64);
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<ComicSeriesDetailRow>(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComicInstallmentDetailRow>(entity => {
            entity.ToTable("comic_installment_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.InstallmentKind)
                .HasColumnName("installment_kind")
                .HasMaxLength(32)
                .HasConversion(
                    value => value.ToCode(),
                    value => value.DecodeAs<ComicInstallmentKind>())
                .IsRequired();
            entity.Property(row => row.PageCount)
                .HasColumnName("page_count")
                .HasDefaultValue(0)
                .IsRequired();
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<ComicInstallmentDetailRow>(row => row.EntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureBooks(ModelBuilder modelBuilder) {
        modelBuilder.Entity<BookDetailRow>(entity => {
            entity.ToTable("book_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.BookType)
                .HasColumnName("book_type")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookType>());
            entity.Property(row => row.Format)
                .HasColumnName("format")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookFormat>());
            entity.Property(row => row.SourceTier)
                .HasColumnName("source_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookSourceTier>())
                .HasDefaultValue(BookSourceTier.Unknown)
                .IsRequired();
            entity.Property(row => row.PageCount).HasColumnName("page_count");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<BookDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookChapterDetailRow>(entity => {
            entity.ToTable("book_chapter_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.PageCount).HasColumnName("page_count");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<BookChapterDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookChapterAudioMappingRow>(entity => {
            entity.ToTable("book_chapter_audio_mappings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.BookId).HasColumnName("book_id");
            entity.Property(row => row.ReadableChapterKey)
                .HasColumnName("readable_chapter_key")
                .HasMaxLength(2048)
                .IsRequired();
            entity.Property(row => row.AudioTrackEntityId).HasColumnName("audio_track_entity_id");
            entity.Property(row => row.Origin)
                .HasColumnName("origin")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookChapterMappingOrigin>())
                .HasDefaultValue(BookChapterMappingOrigin.Manual)
                .IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.BookId, row.ReadableChapterKey }).IsUnique();
            entity.HasIndex(row => new { row.BookId, row.AudioTrackEntityId }).IsUnique();
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.BookId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.AudioTrackEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookReadingChapterRow>(entity => {
            entity.ToTable("book_reading_chapters");
            entity.HasKey(row => new { row.BookId, row.ChapterKey });
            entity.Property(row => row.BookId).HasColumnName("book_id");
            entity.Property(row => row.ChapterKey)
                .HasColumnName("chapter_key")
                .HasMaxLength(2048)
                .IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").IsRequired();
            entity.Property(row => row.Depth).HasColumnName("depth");
            entity.Property(row => row.DisplayOrder).HasColumnName("display_order");
            entity.Property(row => row.SectionIndex).HasColumnName("section_index");
            entity.Property(row => row.StartFraction).HasColumnName("start_fraction");
            entity.Property(row => row.EndFraction).HasColumnName("end_fraction");
            entity.HasIndex(row => new { row.BookId, row.DisplayOrder });
            entity.HasOne<EntityRow>()
                .WithMany()
                .HasForeignKey(row => row.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookContentStateRow>(entity => {
            entity.ToTable("book_content_states");
            entity.HasKey(row => row.BookId);
            entity.Property(row => row.BookId).HasColumnName("book_id");
            entity.Property(row => row.SourceSignature).HasColumnName("source_signature").HasMaxLength(256);
            entity.Property(row => row.MappingSignature).HasColumnName("mapping_signature").HasMaxLength(256);
            entity.Property(row => row.RefreshedAt).HasColumnName("refreshed_at");
            entity.HasOne<EntityRow>()
                .WithOne()
                .HasForeignKey<BookContentStateRow>(row => row.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAudio(ModelBuilder modelBuilder) {
        modelBuilder.Entity<AudioTrackDetailRow>(entity => {
            entity.ToTable("audio_track_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.EmbeddedArtist).HasColumnName("embedded_artist");
            entity.Property(row => row.EmbeddedAlbum).HasColumnName("embedded_album");
            entity.Property(row => row.SectionLabel).HasColumnName("section_label");
            entity.Property(row => row.SectionOrder).HasColumnName("section_order").HasDefaultValue(0);
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<AudioTrackDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
