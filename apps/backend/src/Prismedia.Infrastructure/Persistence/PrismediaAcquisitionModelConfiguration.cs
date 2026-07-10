using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureAcquisitionTables(ModelBuilder modelBuilder) {
        modelBuilder.Entity<IndexerConfigRow>(entity => {
            entity.ToTable("indexer_configs");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<IndexerKind>())
                .IsRequired();
            entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
            entity.Property(row => row.BaseUrl).HasColumnName("base_url").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.Enabled).HasColumnName("enabled");
            entity.Property(row => row.Priority).HasColumnName("priority");
            entity.Property(row => row.Categories).HasColumnName("categories");
            entity.Property(row => row.QueryLimitPerHour).HasColumnName("query_limit_per_hour");
            entity.Property(row => row.SeedRatio).HasColumnName("seed_ratio");
            entity.Property(row => row.SeedTimeMinutes).HasColumnName("seed_time_minutes");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.Kind, row.Enabled });
        });

        modelBuilder.Entity<IndexerStatusRow>(entity => {
            entity.ToTable("indexer_statuses");
            entity.HasKey(row => row.IndexerConfigId);
            entity.Property(row => row.IndexerConfigId).HasColumnName("indexer_config_id").ValueGeneratedNever();
            entity.Property(row => row.EscalationLevel).HasColumnName("escalation_level");
            entity.Property(row => row.DisabledUntil).HasColumnName("disabled_until");
            entity.Property(row => row.LastFailureMessage).HasColumnName("last_failure_message").HasMaxLength(1024);
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<IndexerConfigRow>().WithOne().HasForeignKey<IndexerStatusRow>(row => row.IndexerConfigId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IndexerCredentialRow>(entity => {
            entity.ToTable("indexer_credentials");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.IndexerConfigId).HasColumnName("indexer_config_id");
            entity.Property(row => row.CredentialKey).HasColumnName("credential_key").HasMaxLength(128).IsRequired();
            entity.Property(row => row.EncryptedValue).HasColumnName("encrypted_value").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.IndexerConfigId, row.CredentialKey }).IsUnique();
            entity.HasOne<IndexerConfigRow>().WithMany().HasForeignKey(row => row.IndexerConfigId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadClientConfigRow>(entity => {
            entity.ToTable("download_client_configs");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<DownloadClientKind>())
                .IsRequired();
            entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
            entity.Property(row => row.BaseUrl).HasColumnName("base_url").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.Username).HasColumnName("username").HasMaxLength(256);
            entity.Property(row => row.Category).HasColumnName("category").HasMaxLength(256).IsRequired();
            entity.Property(row => row.Priority).HasColumnName("priority").HasDefaultValue(25);
            entity.Property(row => row.SeedRatio).HasColumnName("seed_ratio");
            entity.Property(row => row.SeedTimeMinutes).HasColumnName("seed_time_minutes");
            entity.Property(row => row.Enabled).HasColumnName("enabled");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.Kind, row.Enabled });
        });

        modelBuilder.Entity<RemotePathMappingRow>(entity => {
            entity.ToTable("remote_path_mappings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.DownloadClientConfigId).HasColumnName("download_client_config_id");
            entity.Property(row => row.RemotePath).HasColumnName("remote_path").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.LocalPath).HasColumnName("local_path").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.DownloadClientConfigId);
            entity.HasOne<DownloadClientConfigRow>().WithMany().HasForeignKey(row => row.DownloadClientConfigId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadClientCredentialRow>(entity => {
            entity.ToTable("download_client_credentials");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.DownloadClientConfigId).HasColumnName("download_client_config_id");
            entity.Property(row => row.CredentialKey).HasColumnName("credential_key").HasMaxLength(128).IsRequired();
            entity.Property(row => row.EncryptedValue).HasColumnName("encrypted_value").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.DownloadClientConfigId, row.CredentialKey }).IsUnique();
            entity.HasOne<DownloadClientConfigRow>().WithMany().HasForeignKey(row => row.DownloadClientConfigId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookAcquisitionProfileRow>(entity => {
            entity.ToTable("book_acquisition_profiles");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityKind>())
                .HasDefaultValue(EntityKind.Book)
                .IsRequired();
            entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
            entity.Property(row => row.IsDefault).HasColumnName("is_default");
            entity.Property(row => row.TargetLibraryRootId).HasColumnName("target_library_root_id");
            entity.Property(row => row.PathTemplate).HasColumnName("path_template").HasMaxLength(1024).IsRequired();
            entity.Property(row => row.ImportMode)
                .HasColumnName("import_mode")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<ImportMode>())
                .HasDefaultValue(ImportMode.Move)
                .IsRequired();
            entity.Property(row => row.DownloadCategory).HasColumnName("download_category").HasMaxLength(256);
            entity.Property(row => row.AllowedFormats).HasColumnName("allowed_formats");
            entity.Property(row => row.AllowedQualities).HasColumnName("allowed_qualities");
            entity.Property(row => row.CutoffQuality).HasColumnName("cutoff_quality").HasMaxLength(64);
            entity.Property(row => row.PreferredLanguages).HasColumnName("preferred_languages");
            entity.Property(row => row.WeightedTermsJson).HasColumnName("weighted_terms_json").HasDefaultValue("[]").IsRequired();
            entity.Property(row => row.MinSeeders).HasColumnName("min_seeders");
            entity.Property(row => row.MinSizeBytes).HasColumnName("min_size_bytes");
            entity.Property(row => row.MaxSizeBytes).HasColumnName("max_size_bytes");
            entity.Property(row => row.RequiredTerms).HasColumnName("required_terms");
            entity.Property(row => row.IgnoredTerms).HasColumnName("ignored_terms");
            entity.Property(row => row.PreferredTerms).HasColumnName("preferred_terms");
            entity.Property(row => row.AutoPick).HasColumnName("auto_pick");
            entity.Property(row => row.AutoRedownload).HasColumnName("auto_redownload");
            entity.Property(row => row.UpgradeUntilCutoff).HasColumnName("upgrade_until_cutoff").HasDefaultValue(false);
            entity.Property(row => row.CutoffSourceTier)
                .HasColumnName("cutoff_source_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookSourceTier>())
                .HasDefaultValue(BookSourceTier.Unknown)
                .IsRequired();
            entity.Property(row => row.CutoffFormatTier)
                .HasColumnName("cutoff_format_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookFormatTier>())
                .HasDefaultValue(BookFormatTier.Unknown)
                .IsRequired();
            entity.Property(row => row.FormatScoresJson).HasColumnName("format_scores_json").HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
            entity.Property(row => row.MinFormatScore).HasColumnName("min_format_score").HasDefaultValue(0);
            entity.Property(row => row.CutoffFormatScore).HasColumnName("cutoff_format_score");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.IsDefault);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.TargetLibraryRootId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomFormatRow>(entity => {
            entity.ToTable("custom_formats");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityKind>())
                .HasDefaultValue(EntityKind.Book)
                .IsRequired();
            entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            entity.Property(row => row.ConditionsJson).HasColumnName("conditions_json").HasColumnType("jsonb").HasDefaultValue("[]").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.Kind);
        });

        modelBuilder.Entity<AcquisitionRow>(entity => {
            entity.ToTable("acquisitions");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityKind>())
                .HasDefaultValue(EntityKind.Book)
                .IsRequired();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.ProfileId).HasColumnName("profile_id");
            entity.Property(row => row.TargetLibraryRootId).HasColumnName("target_library_root_id");
            entity.Property(row => row.Status)
                .HasColumnName("status")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<AcquisitionStatus>())
                .HasDefaultValue(AcquisitionStatus.Pending)
                .IsConcurrencyToken()
                .IsRequired();
            entity.Property(row => row.StatusMessage).HasColumnName("status_message").HasMaxLength(2048);
            entity.Property(row => row.TeardownIntent)
                .HasColumnName("teardown_intent")
                .HasMaxLength(32)
                .HasConversion(
                    value => value == null ? null : value.Value.ToCode(),
                    value => value == null ? null : value.DecodeAs<AcquisitionTeardownIntent>());
            entity.Property(row => row.TeardownOriginalStatus)
                .HasColumnName("teardown_original_status")
                .HasMaxLength(32)
                .HasConversion(
                    value => value == null ? null : value.Value.ToCode(),
                    value => value == null ? null : value.DecodeAs<AcquisitionStatus>());
            entity.Property(row => row.TeardownReplacementAcquisitionId)
                .HasColumnName("teardown_replacement_acquisition_id")
                .IsConcurrencyToken();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(1024).IsRequired();
            entity.Property(row => row.Author).HasColumnName("author").HasMaxLength(512);
            entity.Property(row => row.Series).HasColumnName("series").HasMaxLength(512);
            entity.Property(row => row.SeasonNumber).HasColumnName("season_number");
            entity.Property(row => row.VolumeNumber).HasColumnName("volume_number");
            entity.Property(row => row.EpisodeNumber).HasColumnName("episode_number");
            entity.Property(row => row.Year).HasColumnName("year");
            entity.Property(row => row.PosterUrl).HasColumnName("poster_url").HasMaxLength(2048);
            entity.Property(row => row.Description).HasColumnName("description");
            entity.Property(row => row.IdentityNamespace).HasColumnName("identity_namespace").HasMaxLength(256);
            entity.Property(row => row.IdentityValue).HasColumnName("identity_value").HasMaxLength(256);
            entity.Property(row => row.ExternalIdsJson).HasColumnName("external_ids_json").HasColumnType("jsonb");
            entity.Property(row => row.SourceUrlsJson).HasColumnName("source_urls_json").HasColumnType("jsonb");
            entity.Property(row => row.SelectedReleaseJson).HasColumnName("selected_release_json").HasColumnType("jsonb");
            entity.Property(row => row.FinalSourcePath).HasColumnName("final_source_path").HasMaxLength(2048);
            // Keep the established SQL column name for an additive, data-preserving rollout; the CLR
            // surface is kind-neutral because books, movies, TV, and albums all persist recovery plans.
            entity.Property(row => row.ImportCheckpointJson).HasColumnName("tv_import_checkpoint_json").HasColumnType("jsonb");
            entity.Property(row => row.ImportClaimJobId).HasColumnName("import_claim_job_id");
            entity.Property(row => row.OwnedSourceTier)
                .HasColumnName("owned_source_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookSourceTier>())
                .HasDefaultValue(BookSourceTier.Unknown)
                .IsRequired();
            entity.Property(row => row.OwnedFormatTier)
                .HasColumnName("owned_format_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookFormatTier>())
                .HasDefaultValue(BookFormatTier.Unknown)
                .IsRequired();
            entity.Property(row => row.OwnedMediaQuality).HasColumnName("owned_media_quality").HasMaxLength(64);
            entity.Property(row => row.OwnedMediaRevision).HasColumnName("owned_media_revision").HasDefaultValue(1);
            entity.Property(row => row.OwnedFormatScore).HasColumnName("owned_format_score").HasDefaultValue(0);
            entity.Property(row => row.UpgradeOfAcquisitionId).HasColumnName("upgrade_of_acquisition_id");
            entity.Property(row => row.UpgradeQualityCaptured).HasColumnName("upgrade_quality_captured").HasDefaultValue(false);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.CreatedAt);
            entity.HasIndex(row => row.Status);
            entity.HasIndex(row => row.UpgradeOfAcquisitionId);
            // Loose link into the entity graph: deleting the wanted entity never cascades into (or is
            // blocked by) a transient acquisition attempt.
            entity.HasIndex(row => row.EntityId);
            entity.HasOne<BookAcquisitionProfileRow>().WithMany().HasForeignKey(row => row.ProfileId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.UpgradeOfAcquisitionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReleaseCandidateRow>(entity => {
            entity.ToTable("release_candidates");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.AcquisitionId).HasColumnName("acquisition_id");
            entity.Property(row => row.IndexerConfigId).HasColumnName("indexer_config_id");
            entity.Property(row => row.IndexerName).HasColumnName("indexer_name").HasMaxLength(256).IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.SizeBytes).HasColumnName("size_bytes");
            entity.Property(row => row.Seeders).HasColumnName("seeders");
            entity.Property(row => row.Peers).HasColumnName("peers");
            entity.Property(row => row.Protocol)
                .HasColumnName("protocol")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<DownloadProtocol>())
                .HasDefaultValue(DownloadProtocol.Torrent)
                .IsRequired();
            entity.Property(row => row.DownloadUrl).HasColumnName("download_url").HasMaxLength(4096);
            entity.Property(row => row.MagnetUrl).HasColumnName("magnet_url");
            entity.Property(row => row.InfoHash).HasColumnName("info_hash").HasMaxLength(128);
            entity.Property(row => row.InfoUrl).HasColumnName("info_url").HasMaxLength(4096);
            entity.Property(row => row.PublishedAt).HasColumnName("published_at");
            entity.Property(row => row.Score).HasColumnName("score");
            entity.Property(row => row.Accepted).HasColumnName("accepted");
            entity.Property(row => row.RejectionsJson).HasColumnName("rejections_json").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => new { row.AcquisitionId, row.Score });
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.AcquisitionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<IndexerConfigRow>().WithMany().HasForeignKey(row => row.IndexerConfigId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DownloadTransferRow>(entity => {
            entity.ToTable("download_transfers");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.AcquisitionId).HasColumnName("acquisition_id");
            entity.Property(row => row.DownloadClientConfigId).HasColumnName("download_client_config_id");
            entity.Property(row => row.ClientItemId).HasColumnName("client_item_id").HasMaxLength(256).IsRequired();
            entity.Property(row => row.Category).HasColumnName("category").HasMaxLength(256);
            entity.Property(row => row.SavePath).HasColumnName("save_path").HasMaxLength(2048);
            entity.Property(row => row.ContentPath).HasColumnName("content_path").HasMaxLength(2048);
            entity.Property(row => row.Progress).HasColumnName("progress");
            entity.Property(row => row.State).HasColumnName("state").HasMaxLength(64);
            entity.Property(row => row.StalledSince).HasColumnName("stalled_since");
            entity.Property(row => row.SeedGoalRatio).HasColumnName("seed_goal_ratio");
            entity.Property(row => row.SeedGoalTimeMinutes).HasColumnName("seed_goal_time_minutes");
            entity.Property(row => row.SeedingSince).HasColumnName("seeding_since");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.AcquisitionId);
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.AcquisitionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DownloadClientConfigRow>().WithMany().HasForeignKey(row => row.DownloadClientConfigId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AcquisitionImportHintRow>(entity => {
            entity.ToTable("acquisition_import_hints");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.AcquisitionId).HasColumnName("acquisition_id");
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.SourcePath).HasColumnName("source_path").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.IdentityNamespace).HasColumnName("identity_namespace").HasMaxLength(256);
            entity.Property(row => row.IdentityValue).HasColumnName("identity_value").HasMaxLength(256);
            entity.Property(row => row.ExternalIdsJson).HasColumnName("external_ids_json").HasColumnType("jsonb");
            entity.Property(row => row.SourceUrlsJson).HasColumnName("source_urls_json").HasColumnType("jsonb");
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(1024);
            entity.Property(row => row.Author).HasColumnName("author").HasMaxLength(512);
            entity.Property(row => row.Series).HasColumnName("series").HasMaxLength(512);
            entity.Property(row => row.Year).HasColumnName("year");
            entity.Property(row => row.PosterUrl).HasColumnName("poster_url").HasMaxLength(2048);
            entity.Property(row => row.Description).HasColumnName("description");
            entity.Property(row => row.OwnedSourceTier)
                .HasColumnName("owned_source_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookSourceTier>())
                .HasDefaultValue(BookSourceTier.Unknown)
                .IsRequired();
            entity.Property(row => row.OwnedFormatTier)
                .HasColumnName("owned_format_tier")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BookFormatTier>())
                .HasDefaultValue(BookFormatTier.Unknown)
                .IsRequired();
            entity.Property(row => row.Consumed).HasColumnName("consumed");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.SourcePath);
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.AcquisitionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WantedSuppressionRow>(entity => {
            entity.ToTable("wanted_suppressions");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Provider).HasColumnName("provider").HasMaxLength(256).IsRequired();
            entity.Property(row => row.ItemId).HasColumnName("item_id").HasMaxLength(512).IsRequired();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityKind>())
                .HasDefaultValue(EntityKind.Book)
                .IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(1024).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => new { row.Provider, row.ItemId }).IsUnique();
        });

        modelBuilder.Entity<AcquisitionBlocklistRow>(entity => {
            entity.ToTable("acquisition_blocklist");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Identity).HasColumnName("identity").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.Reason)
                .HasColumnName("reason")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<BlocklistReason>())
                .HasDefaultValue(BlocklistReason.Failed)
                .IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(2048);
            entity.Property(row => row.IndexerName).HasColumnName("indexer_name").HasMaxLength(256);
            entity.Property(row => row.InfoHash).HasColumnName("info_hash").HasMaxLength(128);
            entity.Property(row => row.AcquisitionId).HasColumnName("acquisition_id");
            entity.Property(row => row.Message).HasColumnName("message").HasMaxLength(2048);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => row.Identity).IsUnique();
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.AcquisitionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AcquisitionHistoryRow>(entity => {
            entity.ToTable("acquisition_history");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.AcquisitionId).HasColumnName("acquisition_id");
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityKind>())
                .HasDefaultValue(EntityKind.Book)
                .IsRequired();
            entity.Property(row => row.Event)
                .HasColumnName("event")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<AcquisitionHistoryEvent>())
                .HasDefaultValue(AcquisitionHistoryEvent.Grabbed)
                .IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(1024).IsRequired();
            entity.Property(row => row.ReleaseTitle).HasColumnName("release_title").HasMaxLength(2048);
            entity.Property(row => row.IndexerName).HasColumnName("indexer_name").HasMaxLength(256);
            entity.Property(row => row.DownloadClientName).HasColumnName("download_client_name").HasMaxLength(256);
            entity.Property(row => row.QualityCode).HasColumnName("quality_code").HasMaxLength(64);
            entity.Property(row => row.FormatScore).HasColumnName("format_score");
            entity.Property(row => row.Message).HasColumnName("message").HasMaxLength(2048);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => row.AcquisitionId);
            entity.HasIndex(row => row.EntityId);
            entity.HasIndex(row => row.CreatedAt).IsDescending();
            // SetNull (not Cascade): the activity log must survive the hard delete of its acquisition —
            // the whole point of a durable history is that a grabbed/failed record outlives the item.
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.AcquisitionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MonitorRow>(entity => {
            entity.ToTable("monitors");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kind)
                .HasColumnName("kind")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<EntityKind>())
                .HasDefaultValue(EntityKind.Book)
                .IsRequired();
            entity.Property(row => row.AcquisitionId).HasColumnName("acquisition_id");
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.TargetLibraryRootId).HasColumnName("target_library_root_id");
            entity.Property(row => row.ProfileId).HasColumnName("profile_id");
            entity.Property(row => row.Preset)
                .HasColumnName("monitor_preset")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<MonitorPreset>())
                .HasDefaultValue(MonitorPreset.All)
                .IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<MonitorStatus>())
                .HasDefaultValue(MonitorStatus.Active)
                .IsConcurrencyToken()
                .IsRequired();
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(1024).IsRequired();
            entity.Property(row => row.Author).HasColumnName("author").HasMaxLength(512);
            entity.Property(row => row.LastSearchedAt).HasColumnName("last_searched_at");
            entity.Property(row => row.UpgradeAttempts).HasColumnName("upgrade_attempts").HasDefaultValue(0);
            entity.Property(row => row.BarrenSearches).HasColumnName("barren_searches").HasDefaultValue(0);
            entity.Property(row => row.UpgradeChildAcquisitionId).HasColumnName("upgrade_child_acquisition_id");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.Status, row.LastSearchedAt });
            entity.HasIndex(row => row.AcquisitionId).IsUnique();
            // One stable monitoring intent per Entity across acquisition attempts.
            entity.HasIndex(row => row.EntityId).IsUnique();
            // SetNull (not Cascade): hard-deleting the linked acquisition must auto-pause the monitor, not delete it.
            entity.HasOne<AcquisitionRow>().WithMany().HasForeignKey(row => row.AcquisitionId).OnDelete(DeleteBehavior.SetNull);
            // The in-flight upgrade child is a loose link (no FK), cleared explicitly so deleting it does
            // not cascade into the stable monitor intent.
        });
    }
}
