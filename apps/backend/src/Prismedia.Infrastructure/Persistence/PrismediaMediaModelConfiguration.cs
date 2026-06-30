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
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<GalleryDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.SetNull);
        });

        ConfigureBooks(modelBuilder);
        ConfigureAudio(modelBuilder);
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
            entity.Property(row => row.CoverPageEntityId).HasColumnName("cover_page_entity_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<BookDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BookChapterDetailRow>(entity => {
            entity.ToTable("book_chapter_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.CoverPageEntityId).HasColumnName("cover_page_entity_id");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<BookChapterDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

    }

    private static void ConfigureAudio(ModelBuilder modelBuilder) {
        modelBuilder.Entity<MusicArtistDetailRow>(entity => {
            entity.ToTable("music_artist_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<MusicArtistDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AudioLibraryDetailRow>(entity => {
            entity.ToTable("audio_library_details");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<AudioLibraryDetailRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.SetNull);
        });

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
