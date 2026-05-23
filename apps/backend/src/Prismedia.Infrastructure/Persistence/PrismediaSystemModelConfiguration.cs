using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureSystemTables(ModelBuilder modelBuilder) {
        modelBuilder.Entity<MediaFileIgnoreRow>(entity => {
            entity.ToTable("media_file_ignores");
            entity.HasKey(row => row.Path);
            entity.Property(row => row.Path).HasColumnName("path");
            entity.Property(row => row.EntityKindCode).HasColumnName("entity_kind_code").HasMaxLength(64);
            entity.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(128);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(row => row.EntityKindCode);
        });

        modelBuilder.Entity<UiPreferenceRow>(entity => {
            entity.ToTable("ui_prefs");
            entity.HasKey(row => row.Key);
            entity.Property(row => row.Key).HasColumnName("key");
            entity.Property(row => row.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<AppSettingRow>(entity => {
            entity.ToTable("app_settings");
            entity.HasKey(row => row.Key);
            entity.Property(row => row.Key).HasColumnName("key").HasMaxLength(128);
            entity.Property(row => row.ValueJson).HasColumnName("value_json").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
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
    }
}
