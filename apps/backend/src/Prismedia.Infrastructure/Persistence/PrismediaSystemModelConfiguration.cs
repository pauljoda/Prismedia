using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureSystemTables(ModelBuilder modelBuilder) {
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
            entity.Property(row => row.AutoIdentify).HasColumnName("auto_identify").HasDefaultValue(true);
            entity.Property(row => row.LastScannedAt).HasColumnName("last_scanned_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.Path).IsUnique();
        });

        modelBuilder.Entity<MediaFileIgnoreRow>(entity => {
            entity.ToTable("media_file_ignores");
            entity.HasKey(row => new { row.LibraryRootId, row.Path });
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.Property(row => row.Path).HasColumnName("path");
            entity.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).HasDefaultValue(FileEntryKind.File.ToCode()).IsRequired();
            entity.Property(row => row.EntityKindCode).HasColumnName("entity_kind_code").HasMaxLength(64);
            entity.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(128);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.EntityKindCode);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScannedFileRow>(entity => {
            entity.ToTable("scanned_files");
            entity.HasKey(row => new { row.LibraryRootId, row.ScanKind, row.Path });
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.Property(row => row.ScanKind).HasColumnName("scan_kind").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Path).HasColumnName("path");
            entity.Property(row => row.SizeBytes).HasColumnName("size_bytes");
            entity.Property(row => row.ModifiedTicks).HasColumnName("modified_ticks");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.LibraryRootId, row.ScanKind });
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UiPreferenceRow>(entity => {
            entity.ToTable("ui_prefs");
            entity.HasKey(row => row.Key);
            entity.Property(row => row.Key).HasColumnName("key");
            entity.Property(row => row.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<BrowserSessionRow>(entity => {
            entity.ToTable("browser_sessions");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.LastSeenAt);
        });

        modelBuilder.Entity<BrowserSessionSettingRow>(entity => {
            entity.ToTable("browser_session_settings");
            entity.HasKey(row => new { row.BrowserSessionId, row.Key });
            entity.Property(row => row.BrowserSessionId).HasColumnName("browser_session_id");
            entity.Property(row => row.Key).HasColumnName("key").HasMaxLength(128);
            entity.Property(row => row.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<BrowserSessionRow>().WithMany().HasForeignKey(row => row.BrowserSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSettingRow>(entity => {
            entity.ToTable("app_settings");
            entity.HasKey(row => row.Key);
            entity.Property(row => row.Key).HasColumnName("key").HasMaxLength(128);
            entity.Property(row => row.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<AppSecurityRow>(entity => {
            entity.ToTable("app_security");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ServerId).HasColumnName("server_id");
            entity.Property(row => row.ApiKey).HasColumnName("api_key").HasMaxLength(128).IsRequired();
            entity.Property(row => row.DefaultProfileSeeded).HasColumnName("default_profile_seeded");
            entity.Property(row => row.ApiKeyCreatedAt).HasColumnName("api_key_created_at");
            entity.Property(row => row.ApiKeyUpdatedAt).HasColumnName("api_key_updated_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<JellyfinProfileRow>(entity => {
            entity.ToTable("jellyfin_profiles");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            entity.Property(row => row.NormalizedUsername).HasColumnName("normalized_username").HasMaxLength(64).IsRequired();
            entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
            entity.Property(row => row.AllowSfw).HasColumnName("allow_sfw").HasDefaultValue(true);
            entity.Property(row => row.AllowNsfw).HasColumnName("allow_nsfw");
            entity.Property(row => row.Enabled).HasColumnName("enabled");
            entity.Property(row => row.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.NormalizedUsername).IsUnique();
        });

        modelBuilder.Entity<JellyfinSessionRow>(entity => {
            entity.ToTable("jellyfin_sessions");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ProfileId).HasColumnName("profile_id");
            entity.Property(row => row.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Client).HasColumnName("client").HasMaxLength(128);
            entity.Property(row => row.DeviceName).HasColumnName("device_name").HasMaxLength(128);
            entity.Property(row => row.DeviceId).HasColumnName("device_id").HasMaxLength(256);
            entity.Property(row => row.ApplicationVersion).HasColumnName("application_version").HasMaxLength(64);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(row => row.InvalidatedAt).HasColumnName("invalidated_at");
            entity.HasIndex(row => row.TokenHash).IsUnique();
            entity.HasIndex(row => new { row.ProfileId, row.InvalidatedAt });
            entity.HasOne<JellyfinProfileRow>().WithMany().HasForeignKey(row => row.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderConfigRow>(entity => {
            entity.ToTable("provider_configs");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ProviderCode).HasColumnName("provider_code").HasMaxLength(128).IsRequired();
            entity.Property(row => row.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
            entity.Property(row => row.ProviderType)
                .HasColumnName("provider_type")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<ProviderType>())
                .IsRequired();
            entity.Property(row => row.SettingsJson).HasColumnName("settings_json").HasColumnType("jsonb");
            entity.Property(row => row.Enabled).HasColumnName("enabled");
            entity.Property(row => row.IsNsfw).HasColumnName("is_nsfw");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => row.ProviderCode).IsUnique();
        });

        modelBuilder.Entity<ProviderCredentialRow>(entity => {
            entity.ToTable("provider_credentials");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ProviderConfigId).HasColumnName("provider_config_id");
            entity.Property(row => row.CredentialKey).HasColumnName("credential_key").HasMaxLength(128).IsRequired();
            entity.Property(row => row.EncryptedValue).HasColumnName("encrypted_value").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.ProviderConfigId, row.CredentialKey }).IsUnique();
            entity.HasOne<ProviderConfigRow>().WithMany().HasForeignKey(row => row.ProviderConfigId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentifyResultRow>(entity => {
            entity.ToTable("identify_results");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.ProviderConfigId).HasColumnName("provider_config_id");
            entity.Property(row => row.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<IdentifyResultStatus>())
                .IsRequired();
            entity.Property(row => row.MatchType).HasColumnName("match_type").HasMaxLength(64);
            entity.Property(row => row.RawResultJson).HasColumnName("raw_result_json").HasColumnType("jsonb");
            entity.Property(row => row.ProposedResultJson).HasColumnName("proposed_result_json").HasColumnType("jsonb");
            entity.Property(row => row.AppliedAt).HasColumnName("applied_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(row => new { row.EntityId, row.Status });
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ProviderConfigRow>().WithMany().HasForeignKey(row => row.ProviderConfigId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IdentifyQueueItemRow>(entity => {
            entity.ToTable("identify_queue_items");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.State)
                .HasColumnName("state")
                .HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<IdentifyQueueState>())
                .IsRequired();
            entity.Property(row => row.ProviderCode).HasColumnName("provider_code").HasMaxLength(128);
            entity.Property(row => row.Action)
                .HasColumnName("action")
                .HasMaxLength(128)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<IdentifyAction>())
                .IsRequired();
            entity.Property(row => row.QueryJson).HasColumnName("query_json").HasColumnType("jsonb");
            entity.Property(row => row.CandidatesJson).HasColumnName("candidates_json").HasColumnType("jsonb");
            entity.Property(row => row.ProposalJson).HasColumnName("proposal_json").HasColumnType("jsonb");
            entity.Property(row => row.Error).HasColumnName("error");
            entity.Property(row => row.CascadeJobId).HasColumnName("cascade_job_id");
            entity.Property(row => row.SearchJobId).HasColumnName("search_job_id");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.CompletedAt).HasColumnName("completed_at");
            entity.HasIndex(row => row.EntityId).IsUnique();
            entity.HasIndex(row => new { row.State, row.UpdatedAt });
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FingerprintSubmissionRow>(entity => {
            entity.ToTable("fingerprint_submissions");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.ProviderConfigId).HasColumnName("provider_config_id");
            entity.Property(row => row.Algorithm).HasColumnName("algorithm").HasMaxLength(64).IsRequired();
            entity.Property(row => row.Hash).HasColumnName("hash").IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status")
                .HasMaxLength(64)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<FingerprintSubmissionStatus>())
                .IsRequired();
            entity.Property(row => row.Error).HasColumnName("error");
            entity.Property(row => row.SubmittedAt).HasColumnName("submitted_at");
            entity.HasIndex(row => new { row.EntityId, row.Algorithm, row.Hash }).IsUnique();
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ProviderConfigRow>().WithMany().HasForeignKey(row => row.ProviderConfigId).OnDelete(DeleteBehavior.SetNull);
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
            entity.Property(row => row.IsManual).HasColumnName("is_manual");
            entity.Property(row => row.SizeBytes).HasColumnName("size_bytes");
            entity.Property(row => row.Error).HasColumnName("error");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.CompletedAt).HasColumnName("completed_at");
            entity.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            entity.HasIndex(row => new { row.IsManual, row.ExpiresAt });
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
            entity.Property(row => row.Lane)
                .HasColumnName("lane")
                .HasMaxLength(64)
                .HasConversion(
                    value => value == null ? null : value.Value.ToCode(),
                    value => value == null ? null : value.DecodeAs<JobRunLane>());
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
            entity.HasIndex(row => new { row.Status, row.Lane, row.AvailableAt, row.Priority });
            entity.HasIndex(row => new { row.Type, row.TargetEntityId, row.Status })
                .HasDatabaseName("ix_job_runs_dedup");
            entity.HasIndex(row => new { row.Type, row.TargetEntityId })
                .IsUnique()
                .HasFilter("status IN ('queued', 'running') AND target_entity_id IS NOT NULL")
                .HasDatabaseName("ux_job_runs_pending_type_target");
            entity.ToTable(table => {
                table.HasCheckConstraint("ck_job_runs_progress", "progress >= 0 AND progress <= 100");
                table.HasCheckConstraint("ck_job_runs_attempts", "attempts >= 0 AND max_attempts > 0");
            });
        });
    }
}
