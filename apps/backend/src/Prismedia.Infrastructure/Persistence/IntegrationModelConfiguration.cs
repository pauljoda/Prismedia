using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Persistence;

internal static partial class PrismediaModelConfiguration {
    private static void ConfigureIntegrationTables(ModelBuilder modelBuilder) {
        modelBuilder.Entity<ManagedRequestRow>(entity => {
            entity.ToTable("managed_requests");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ConnectionId).HasColumnName("connection_id");
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
            entity.Property(row => row.Phase).HasColumnName("phase").HasMaxLength(32).HasConversion(value => value.ToCode(), value => value.DecodeAs<ManagedRequestPhase>());
            entity.Property(row => row.StateJson).HasColumnName("state").HasColumnType("jsonb");
            entity.Property(row => row.PlanJson).HasColumnName("plan").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.NextCheckAt).HasColumnName("next_check_at");
            entity.Property(row => row.Problem).HasColumnName("problem").HasMaxLength(4096);
            entity.HasOne<IntegrationConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.NextCheckAt);
            entity.HasIndex(row => new { row.ConnectionId, row.CreatedAt });
        });
        modelBuilder.Entity<ManagedControlRow>(entity => {
            entity.ToTable("managed_controls");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ConnectionId).HasColumnName("connection_id");
            entity.Property(row => row.HoldingId).HasColumnName("holding_id");
            entity.Property(row => row.ActiveHoldingId).HasColumnName("active_holding_id");
            entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
            entity.Property(row => row.StateJson).HasColumnName("state").HasColumnType("jsonb");
            entity.Property(row => row.PlanJson).HasColumnName("plan").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.NextCheckAt).HasColumnName("next_check_at");
            entity.Property(row => row.Problem).HasColumnName("problem").HasMaxLength(4096);
            entity.HasOne<IntegrationConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ManagedHoldingRow>().WithMany().HasForeignKey(row => row.HoldingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.ActiveHoldingId).IsUnique();
            entity.HasIndex(row => new { row.HoldingId, row.CreatedAt });
            entity.HasIndex(row => row.NextCheckAt);
        });
        modelBuilder.Entity<FulfillmentReservationRow>(entity => {
            entity.ToTable("fulfillment_reservations");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.OwnerId).HasColumnName("owner_id");
            entity.Property(row => row.OwnerKind).HasColumnName("owner_kind").HasMaxLength(32).HasConversion(value => value.ToCode(), value => value.DecodeAs<FulfillmentOwnerKind>());
            entity.Property(row => row.ConnectionId).HasColumnName("connection_id");
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.BookRendition).HasColumnName("book_rendition").HasMaxLength(32).HasConversion(value => value.HasValue ? value.Value.ToCode() : null, value => value == null ? null : value.DecodeAs<BookRendition>());
            entity.Property(row => row.ExternalIdsJson).HasColumnName("external_ids").HasColumnType("jsonb");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.ReleasedAt).HasColumnName("released_at");
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<IntegrationConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.OwnerId, row.OwnerKind, row.EntityId, row.BookRendition }).IsUnique().AreNullsDistinct(false);
            entity.HasIndex(row => row.EntityId);
        });
        modelBuilder.Entity<ManagedHoldingRow>(entity => {
            entity.ToTable("managed_holdings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ConnectionId).HasColumnName("connection_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(64).HasConversion(value => value.ToCode(), value => EntityKindRegistry.Require(value));
            entity.Property(row => row.RemoteId).HasColumnName("remote_id").HasMaxLength(512);
            entity.Property(row => row.Title).HasColumnName("title").HasMaxLength(512);
            entity.Property(row => row.ItemJson).HasColumnName("item").HasColumnType("jsonb");
            entity.Property(row => row.SelectionsJson).HasColumnName("selections").HasColumnType("jsonb");
            entity.Property(row => row.TargetsJson).HasColumnName("targets").HasColumnType("jsonb");
            entity.Property(row => row.PeopleEnrichmentFingerprint).HasColumnName("people_enrichment_fingerprint").HasMaxLength(64);
            entity.Property(row => row.PeopleEnrichmentCompletedAt).HasColumnName("people_enrichment_completed_at");
            entity.Property(row => row.ReleaseOperationId).HasColumnName("release_operation_id");
            entity.Property(row => row.ReleaseRequestJson).HasColumnName("release_request").HasColumnType("jsonb");
            entity.Property(row => row.ReleasedAt).HasColumnName("released_at");
            entity.Property(row => row.ReleasedBindingsJson).HasColumnName("released_bindings").HasColumnType("jsonb").HasDefaultValue("[]");
            entity.HasIndex(row => row.ReleaseOperationId).IsUnique();
            entity.Property(row => row.Status).HasColumnName("status").HasMaxLength(32).HasConversion(value => value.ToCode(), value => value.DecodeAs<ManagedTrackingStatus>());
            entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
            entity.Property(row => row.LastCheckedAt).HasColumnName("last_checked_at");
            entity.Property(row => row.NextCheckAt).HasColumnName("next_check_at");
            entity.Property(row => row.Problem).HasColumnName("problem").HasMaxLength(4096);
            entity.HasOne<IntegrationConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.ConnectionId, row.Kind, row.RemoteId }).IsUnique().HasFilter("released_at IS NULL");
            entity.HasIndex(row => row.NextCheckAt);
        });
        modelBuilder.Entity<ManagedSourceBindingRow>(entity => {
            entity.ToTable("managed_source_bindings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.HoldingId).HasColumnName("holding_id");
            entity.Property(row => row.RemoteTargetId).HasColumnName("remote_target_id").HasMaxLength(512);
            entity.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(64).HasConversion(value => value.ToCode(), value => EntityKindRegistry.Require(value));
            entity.Property(row => row.SeasonNumber).HasColumnName("season_number");
            entity.Property(row => row.EpisodeNumber).HasColumnName("episode_number");
            entity.Property(row => row.AbsoluteNumber).HasColumnName("absolute_number");
            entity.Property(row => row.IssueLabel).HasColumnName("issue_label").HasMaxLength(128);
            entity.Property(row => row.EntityId).HasColumnName("entity_id");
            entity.Property(row => row.SourceFileId).HasColumnName("source_file_id");
            entity.Property(row => row.RemoteFileId).HasColumnName("remote_file_id").HasMaxLength(512);
            entity.Property(row => row.LocalPath).HasColumnName("local_path").HasMaxLength(8192);
            entity.Property(row => row.SizeBytes).HasColumnName("size_bytes");
            entity.Property(row => row.WrittenAt).HasColumnName("written_at");
            entity.Property(row => row.IsAvailable).HasColumnName("is_available");
            entity.HasOne<ManagedHoldingRow>().WithMany().HasForeignKey(row => row.HoldingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EntityRow>().WithMany().HasForeignKey(row => row.EntityId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EntityFileRow>().WithMany().HasForeignKey(row => row.SourceFileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.HoldingId, row.RemoteTargetId }).IsUnique();
            entity.HasIndex(row => row.EntityId).IsUnique();
            entity.HasIndex(row => row.SourceFileId).IsUnique();
        });
        modelBuilder.Entity<ExternalLibraryMountRow>(entity => {
            entity.ToTable("external_library_mounts");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ConnectionId).HasColumnName("connection_id");
            entity.Property(row => row.LibraryRootId).HasColumnName("library_root_id");
            entity.Property(row => row.RemoteRootId).HasColumnName("remote_root_id").HasMaxLength(512);
            entity.Property(row => row.RemotePath).HasColumnName("remote_path").HasMaxLength(8192);
            entity.Property(row => row.LocalPath).HasColumnName("local_path").HasMaxLength(8192);
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.HasOne<IntegrationConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LibraryRootRow>().WithMany().HasForeignKey(row => row.LibraryRootId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => row.LibraryRootId).IsUnique();
            entity.HasIndex(row => new { row.ConnectionId, row.RemoteRootId }).IsUnique();
        });
        modelBuilder.Entity<IntegrationConnectionRow>(entity => {
            entity.ToTable("integration_connections");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.PluginId).HasColumnName("plugin_id").HasMaxLength(128).IsRequired();
            entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(row => row.BaseUrl).HasColumnName("base_url").HasMaxLength(2048).IsRequired();
            entity.Property(row => row.Enabled).HasColumnName("enabled");
            entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
            entity.Property(row => row.Status).HasColumnName("status").HasMaxLength(32)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<ConnectionStatus>());
            entity.Property(row => row.EnabledCapabilitiesJson).HasColumnName("enabled_capabilities").HasColumnType("jsonb");
            entity.Property(row => row.SettingsJson).HasColumnName("settings").HasColumnType("jsonb");
            entity.Property(row => row.ProtectedSecretsJson).HasColumnName("protected_secrets").HasColumnType("jsonb");
            entity.Property(row => row.EffectiveCapabilitiesJson).HasColumnName("effective_capabilities").HasColumnType("jsonb");
            entity.Property(row => row.RemoteInstanceId).HasColumnName("remote_instance_id").HasMaxLength(512);
            entity.Property(row => row.HasPersistentRemoteIdentity).HasColumnName("has_persistent_remote_identity");
            entity.Property(row => row.LastCheckedAt).HasColumnName("last_checked_at");
            entity.Property(row => row.LastError).HasColumnName("last_error").HasMaxLength(4096);
            entity.HasIndex(row => row.PluginId); // Multiple instances of the same plugin are intentional.
        });
        modelBuilder.Entity<IntegrationTransferRow>(entity => {
            entity.ToTable("integration_transfers");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ConnectionId).HasColumnName("connection_id");
            entity.HasOne<IntegrationConnectionRow>().WithMany().HasForeignKey(row => row.ConnectionId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(row => row.Revision).HasColumnName("revision").IsConcurrencyToken();
            entity.Property(row => row.Phase).HasColumnName("phase").HasMaxLength(40)
                .HasConversion(value => value.ToCode(), value => value.DecodeAs<IntegrationTransferPhase>());
            entity.Property(row => row.StateJson).HasColumnName("state").HasColumnType("jsonb");
            entity.Property(row => row.ProtectedPlan).HasColumnName("protected_plan");
            entity.Property(row => row.ActiveOwnershipKey).HasColumnName("active_ownership_key").HasMaxLength(256);
            entity.HasIndex(row => row.ActiveOwnershipKey).IsUnique();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.LastError).HasColumnName("last_error").HasMaxLength(4096);
            entity.HasIndex(row => row.CreatedAt);
        });
    }
}
