using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureEntityCapabilities(ModelBuilder modelBuilder) {
        modelBuilder.Entity<EntityDescriptionRow>(entity => {
            entity.ToTable("entity_descriptions");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Value).HasColumnName("value").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<EntityDescriptionRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityPlaybackRow>(entity => {
            entity.ToTable("entity_playback");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.PlayCount).HasColumnName("play_count");
            entity.Property(row => row.PlayDurationSeconds).HasColumnName("play_duration_seconds");
            entity.Property(row => row.ResumeSeconds).HasColumnName("resume_seconds");
            entity.Property(row => row.LastPlayedAt).HasColumnName("last_played_at");
            entity.Property(row => row.CompletedAt).HasColumnName("completed_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<EntityPlaybackRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityStatRow>(entity => {
            entity.ToTable("entity_stats");
            entity.HasKey(row => new { row.EntityId, row.Code });
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Value).HasColumnName("value");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint("ck_entity_stats_value", "value >= 0"));
        });

        modelBuilder.Entity<EntityDateRow>(entity => {
            entity.ToTable("entity_dates");
            entity.HasKey(row => new { row.EntityId, row.Code });
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Value).HasColumnName("value").HasMaxLength(256).IsRequired();
            entity.Property(row => row.SortableValue).HasColumnName("sortable_value");
            entity.Property(row => row.Precision).HasColumnName("precision").HasMaxLength(32);
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityTechnicalRow>(entity => {
            entity.ToTable("entity_technical");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(row => row.Width).HasColumnName("width");
            entity.Property(row => row.Height).HasColumnName("height");
            entity.Property(row => row.FrameRate).HasColumnName("frame_rate");
            entity.Property(row => row.BitRate).HasColumnName("bit_rate");
            entity.Property(row => row.SampleRate).HasColumnName("sample_rate");
            entity.Property(row => row.Channels).HasColumnName("channels");
            entity.Property(row => row.Codec).HasColumnName("codec").HasMaxLength(128);
            entity.Property(row => row.Container).HasColumnName("container").HasMaxLength(128);
            entity.Property(row => row.Format).HasColumnName("format").HasMaxLength(128);
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<EntityTechnicalRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntitySourceRow>(entity => {
            entity.ToTable("entity_sources");
            entity.HasKey(row => new { row.EntityId, row.Code });
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Value).HasColumnName("value").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityProgressRow>(entity => {
            entity.ToTable("entity_progress");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.CurrentEntityId).HasColumnName("current_entity_id");
            entity.Property(row => row.Unit).HasColumnName("unit").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Index).HasColumnName("index");
            entity.Property(row => row.Total).HasColumnName("total");
            entity.Property(row => row.Mode).HasColumnName("mode").HasMaxLength(64);
            entity.Property(row => row.Location).HasColumnName("location");
            entity.Property(row => row.CompletedAt).HasColumnName("completed_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<EntityProgressRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.CurrentEntityId).OnDelete(DeleteBehavior.SetNull);
            entity.ToTable(table => table.HasCheckConstraint("ck_entity_progress_bounds", "index >= 0 AND total >= 0"));
        });

        modelBuilder.Entity<EntityPositionRow>(entity => {
            entity.ToTable("entity_positions");
            entity.HasKey(row => new { row.EntityId, row.Code });
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Value).HasColumnName("value");
            entity.Property(row => row.Label).HasColumnName("label").HasMaxLength(256);
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityClassificationRow>(entity => {
            entity.ToTable("entity_classifications");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Value).HasColumnName("value").HasMaxLength(128);
            entity.Property(row => row.System).HasColumnName("system").HasMaxLength(128);
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<EntityClassificationRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityLifetimeRow>(entity => {
            entity.ToTable("entity_lifetimes");
            entity.HasKey(row => row.EntityId);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.StartCode).HasColumnName("start_code").HasMaxLength(64);
            entity.Property(row => row.StartValue).HasColumnName("start_value").HasMaxLength(256);
            entity.Property(row => row.StartSortableValue).HasColumnName("start_sortable_value");
            entity.Property(row => row.StartPrecision).HasColumnName("start_precision").HasMaxLength(32);
            entity.Property(row => row.EndCode).HasColumnName("end_code").HasMaxLength(64);
            entity.Property(row => row.EndValue).HasColumnName("end_value").HasMaxLength(256);
            entity.Property(row => row.EndSortableValue).HasColumnName("end_sortable_value");
            entity.Property(row => row.EndPrecision).HasColumnName("end_precision").HasMaxLength(32);
            entity.Property(row => row.Label).HasColumnName("label").HasMaxLength(256);
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<EntityRow>().WithOne().HasForeignKey<EntityLifetimeRow>(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityFileFingerprintRow>(entity => {
            entity.ToTable("entity_file_fingerprints");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.EntityFileId).HasColumnName("entity_file_id");
            entity.Property(row => row.Algorithm).HasColumnName("algorithm").HasMaxLength(64).IsRequired()
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<FingerprintAlgorithm>());
            entity.Property(row => row.Value).HasColumnName("value").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => new { row.EntityId, row.Algorithm }).IsUnique();
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityFileRow>().WithMany().HasForeignKey(row => row.EntityFileId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
