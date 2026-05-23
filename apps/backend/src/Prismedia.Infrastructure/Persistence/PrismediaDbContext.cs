using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

/// <summary>
/// Entity Framework context for the Prismedia persistence model.
/// </summary>
public sealed class PrismediaDbContext : DbContext {
    /// <summary>
    /// Creates the context with provider-specific options supplied by dependency injection.
    /// </summary>
    /// <param name="options">EF Core context options, including the PostgreSQL connection and provider configuration.</param>
    public PrismediaDbContext(DbContextOptions<PrismediaDbContext> options)
        : base(options) {
    }

    public DbSet<EntityKindRow> EntityKinds => Set<EntityKindRow>();

    public DbSet<EntityRow> Entities => Set<EntityRow>();

    public DbSet<EntityRatingRow> EntityRatings => Set<EntityRatingRow>();

    public DbSet<EntityFlagRow> EntityFlags => Set<EntityFlagRow>();

    public DbSet<EntityDescriptionRow> EntityDescriptions => Set<EntityDescriptionRow>();

    public DbSet<EntityRelationshipLinkRow> EntityRelationshipLinks => Set<EntityRelationshipLinkRow>();


    public DbSet<EntityUrlRow> EntityUrls => Set<EntityUrlRow>();

    public DbSet<EntityExternalIdRow> EntityExternalIds => Set<EntityExternalIdRow>();

    public DbSet<EntityMarkerRow> EntityMarkers => Set<EntityMarkerRow>();

    public DbSet<EntitySubtitleRow> EntitySubtitles => Set<EntitySubtitleRow>();

    public DbSet<EntityFileRow> EntityFiles => Set<EntityFileRow>();

    public DbSet<EntityFileFingerprintRow> EntityFileFingerprints => Set<EntityFileFingerprintRow>();

    public DbSet<EntityPlaybackRow> EntityPlayback => Set<EntityPlaybackRow>();

    public DbSet<EntityStatRow> EntityStats => Set<EntityStatRow>();

    public DbSet<EntityDateRow> EntityDates => Set<EntityDateRow>();

    public DbSet<EntityTechnicalRow> EntityTechnical => Set<EntityTechnicalRow>();

    public DbSet<MediaSourceRow> MediaSources => Set<MediaSourceRow>();

    public DbSet<MediaStreamRow> MediaStreams => Set<MediaStreamRow>();

    public DbSet<TrickplayInfoRow> TrickplayInfos => Set<TrickplayInfoRow>();

    public DbSet<EntitySourceRow> EntitySources => Set<EntitySourceRow>();

    public DbSet<EntityProgressRow> EntityProgress => Set<EntityProgressRow>();

    public DbSet<EntityPositionRow> EntityPositions => Set<EntityPositionRow>();

    public DbSet<EntityClassificationRow> EntityClassifications => Set<EntityClassificationRow>();

    public DbSet<EntityLifetimeRow> EntityLifetimes => Set<EntityLifetimeRow>();

    public DbSet<VideoDetailRow> VideoDetails => Set<VideoDetailRow>();

    public DbSet<VideoSeriesDetailRow> VideoSeriesDetails => Set<VideoSeriesDetailRow>();

    public DbSet<GalleryDetailRow> GalleryDetails => Set<GalleryDetailRow>();

    public DbSet<BookDetailRow> BookDetails => Set<BookDetailRow>();

    public DbSet<BookChapterDetailRow> BookChapterDetails => Set<BookChapterDetailRow>();

    public DbSet<AudioLibraryDetailRow> AudioLibraryDetails => Set<AudioLibraryDetailRow>();

    public DbSet<AudioTrackDetailRow> AudioTrackDetails => Set<AudioTrackDetailRow>();

    public DbSet<PersonDetailRow> PersonDetails => Set<PersonDetailRow>();

    public DbSet<TagDetailRow> TagDetails => Set<TagDetailRow>();

    public DbSet<CollectionDetailRow> CollectionDetails => Set<CollectionDetailRow>();

    public DbSet<CollectionItemDetailRow> CollectionItemDetails => Set<CollectionItemDetailRow>();

    public DbSet<LibraryRootRow> LibraryRoots => Set<LibraryRootRow>();

    public DbSet<MediaFileIgnoreRow> MediaFileIgnores => Set<MediaFileIgnoreRow>();

    public DbSet<LibrarySettingsRow> LibrarySettings => Set<LibrarySettingsRow>();

    public DbSet<UiPreferenceRow> UiPreferences => Set<UiPreferenceRow>();

    public DbSet<ProviderConfigRow> ProviderConfigs => Set<ProviderConfigRow>();

    public DbSet<ProviderCredentialRow> ProviderCredentials => Set<ProviderCredentialRow>();

    public DbSet<IdentifyResultRow> IdentifyResults => Set<IdentifyResultRow>();

    public DbSet<FingerprintSubmissionRow> FingerprintSubmissions => Set<FingerprintSubmissionRow>();

    public DbSet<DatabaseBackupRow> DatabaseBackups => Set<DatabaseBackupRow>();

    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {

        modelBuilder.ConfigureBaseEntityModel();
        modelBuilder.ConfigureEntityRelationshipModel();
        modelBuilder.ConfigureEntityAttachmentModel();
        modelBuilder.ConfigurePrismediaModel();

        modelBuilder.Entity<LibraryRootRow>(entity => {
            entity.ToTable("library_roots");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Path).HasColumnName("path").IsRequired();
            entity.Property(row => row.Label).HasColumnName("label").IsRequired();
            entity.Property(row => row.Enabled).HasColumnName("enabled");
            entity.Property(row => row.Recursive).HasColumnName("recursive");
            entity.Property(row => row.ScanVideos).HasColumnName("scan_videos");
            entity.Property(row => row.ScanImages).HasColumnName("scan_images");
            entity.Property(row => row.ScanAudio).HasColumnName("scan_audio");
            entity.Property(row => row.ScanBooks).HasColumnName("scan_books");
            entity.Property(row => row.IsNsfw).HasColumnName("is_nsfw");
            entity.Property(row => row.LastScannedAt).HasColumnName("last_scanned_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.Path).IsUnique();
        });

        modelBuilder.Entity<LibrarySettingsRow>(entity => {
            entity.ToTable("library_settings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.AutoScanEnabled).HasColumnName("auto_scan_enabled");
            entity.Property(row => row.ScanIntervalMinutes).HasColumnName("scan_interval_minutes");
            entity.Property(row => row.AutoGenerateMetadata).HasColumnName("auto_generate_metadata");
            entity.Property(row => row.AutoGenerateFingerprints).HasColumnName("auto_generate_fingerprints");
            entity.Property(row => row.GeneratePhash).HasColumnName("generate_phash");
            entity.Property(row => row.AutoGeneratePreview).HasColumnName("auto_generate_preview");
            entity.Property(row => row.GenerateTrickplay).HasColumnName("generate_trickplay");
            entity.Property(row => row.TrickplayIntervalSeconds).HasColumnName("trickplay_interval_seconds");
            entity.Property(row => row.PreviewClipDurationSeconds).HasColumnName("preview_clip_duration_seconds");
            entity.Property(row => row.ThumbnailQuality).HasColumnName("thumbnail_quality");
            entity.Property(row => row.TrickplayQuality).HasColumnName("trickplay_quality");
            entity.Property(row => row.BackgroundWorkerConcurrency).HasColumnName("background_worker_concurrency");
            entity.Property(row => row.NsfwLanAutoEnable).HasColumnName("nsfw_lan_auto_enable");
            entity.Property(row => row.HideNsfw).HasColumnName("hide_nsfw");
            entity.Property(row => row.MetadataStorageDedicated).HasColumnName("metadata_storage_dedicated");
            entity.Property(row => row.SubtitlesAutoEnable).HasColumnName("subtitles_auto_enable");
            entity.Property(row => row.SubtitlesPreferredLanguages).HasColumnName("subtitles_preferred_languages");
            entity.Property(row => row.AudioPreferredLanguages).HasColumnName("audio_preferred_languages");
            entity.Property(row => row.SubtitleStyle)
                .HasColumnName("subtitle_style")
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<SubtitleStyle>());
            entity.Property(row => row.SubtitleFontScale).HasColumnName("subtitle_font_scale");
            entity.Property(row => row.SubtitlePositionPercent).HasColumnName("subtitle_position_percent");
            entity.Property(row => row.SubtitleOpacity).HasColumnName("subtitle_opacity");
            entity.Property(row => row.DefaultPlaybackMode)
                .HasColumnName("default_playback_mode")
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<PlaybackMode>());
            entity.Property(row => row.ShowCastControls).HasColumnName("show_cast_controls");
            entity.Property(row => row.HlsTranscoderProfile).HasColumnName("hls_transcoder_profile").HasDefaultValue("Software");
            entity.Property(row => row.HlsFfmpegPath).HasColumnName("hls_ffmpeg_path").HasDefaultValue("ffmpeg");
            entity.Property(row => row.HlsVaapiDevice).HasColumnName("hls_vaapi_device").HasDefaultValue("/dev/dri/renderD128");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DatabaseBackupRow>(entity => {
            entity.ToTable("database_backups");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.BackupPath).HasColumnName("backup_path").IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<DatabaseBackupStatus>())
                .IsRequired();
            entity.Property(row => row.Error).HasColumnName("error");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.CompletedAt).HasColumnName("completed_at");
        });

        modelBuilder.Entity<JobRunRow>(entity => {
            entity.ToTable("job_runs");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Type)
                .HasColumnName("type")
                .HasMaxLength(128)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<JobType>())
                .IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<JobRunStatus>())
                .IsRequired();
            entity.Property(row => row.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Priority).HasColumnName("priority");
            entity.Property(row => row.Attempts).HasColumnName("attempts");
            entity.Property(row => row.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(row => row.Progress).HasColumnName("progress");
            entity.Property(row => row.Message).HasColumnName("message");
            entity.Property(row => row.TargetEntityKind).HasColumnName("target_entity_kind").HasMaxLength(64);
            entity.Property(row => row.TargetEntityId).HasColumnName("target_entity_id").HasMaxLength(64);
            entity.Property(row => row.TargetLabel).HasColumnName("target_label").HasMaxLength(512);
            entity.Property(row => row.AvailableAt).HasColumnName("available_at");
            entity.Property(row => row.LockedAt).HasColumnName("locked_at");
            entity.Property(row => row.LockedBy).HasColumnName("locked_by").HasMaxLength(128);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.StartedAt).HasColumnName("started_at");
            entity.Property(row => row.FinishedAt).HasColumnName("finished_at");
            entity.HasIndex(row => new { row.Status, row.AvailableAt, row.Priority });
            entity.HasIndex(row => new { row.Type, row.TargetEntityId, row.Status })
                .HasDatabaseName("ix_job_runs_dedup");
            entity.ToTable(table => {
                table.HasCheckConstraint("ck_job_runs_progress", "progress >= 0 AND progress <= 100");
                table.HasCheckConstraint("ck_job_runs_attempts", "attempts >= 0 AND max_attempts > 0");
            });
        });
    }
}
