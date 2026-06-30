using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Tests;

public sealed class PrismediaDbContextModelTests {
    [Theory]
    [InlineData(typeof(EntityKindRow), "entity_kinds")]
    [InlineData(typeof(EntityRow), "entities")]
    [InlineData(typeof(EntityDescriptionRow), "entity_descriptions")]
    [InlineData(typeof(EntityRelationshipLinkRow), "entity_relationship_links")]
    [InlineData(typeof(EntityPlaybackRow), "entity_playback")]
    [InlineData(typeof(EntityStatRow), "entity_stats")]
    [InlineData(typeof(EntityDateRow), "entity_dates")]
    [InlineData(typeof(EntityTechnicalRow), "entity_technical")]
    [InlineData(typeof(MediaSourceRow), "media_sources")]
    [InlineData(typeof(MediaStreamRow), "media_streams")]
    [InlineData(typeof(TrickplayInfoRow), "trickplay_infos")]
    [InlineData(typeof(EntitySourceRow), "entity_sources")]
    [InlineData(typeof(EntityProgressRow), "entity_progress")]
    [InlineData(typeof(EntityPositionRow), "entity_positions")]
    [InlineData(typeof(EntityClassificationRow), "entity_classifications")]
    [InlineData(typeof(EntityFileFingerprintRow), "entity_file_fingerprints")]
    [InlineData(typeof(VideoDetailRow), "video_details")]
    [InlineData(typeof(VideoSeriesDetailRow), "video_series_details")]
    [InlineData(typeof(GalleryDetailRow), "gallery_details")]
    [InlineData(typeof(BookDetailRow), "book_details")]
    [InlineData(typeof(BookChapterDetailRow), "book_chapter_details")]
    [InlineData(typeof(AudioTrackDetailRow), "audio_track_details")]
    [InlineData(typeof(PersonDetailRow), "person_details")]
    [InlineData(typeof(TagDetailRow), "tag_details")]
    [InlineData(typeof(CollectionDetailRow), "collection_details")]
    [InlineData(typeof(CollectionItemDetailRow), "collection_item_details")]
    [InlineData(typeof(LibraryRootRow), "library_roots")]
    [InlineData(typeof(MediaFileIgnoreRow), "media_file_ignores")]
    [InlineData(typeof(AppSettingRow), "app_settings")]
    [InlineData(typeof(UiPreferenceRow), "ui_prefs")]
    [InlineData(typeof(BrowserSessionRow), "browser_sessions")]
    [InlineData(typeof(BrowserSessionSettingRow), "browser_session_settings")]
    [InlineData(typeof(ProviderConfigRow), "provider_configs")]
    [InlineData(typeof(ProviderCredentialRow), "provider_credentials")]
    [InlineData(typeof(IdentifyResultRow), "identify_results")]
    [InlineData(typeof(IdentifyQueueItemRow), "identify_queue_items")]
    [InlineData(typeof(FingerprintSubmissionRow), "fingerprint_submissions")]
    [InlineData(typeof(DatabaseBackupRow), "database_backups")]
    [InlineData(typeof(JobRunRow), "job_runs")]
    public void ModelMapsGlobalEntityTablesToDefaultSchema(Type entityType, string tableName) {
        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(entityType);

        Assert.NotNull(modelEntity);
        Assert.Null(modelEntity.GetSchema());
        Assert.Equal(tableName, modelEntity.GetTableName());
    }

    [Fact]
    public void EntityKindSeedDataIncludesStructuralHierarchyKinds() {
        using var db = CreateContext();
        var modelEntity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EntityKindRow));

        var seededCodes = modelEntity!.GetSeedData()
            .Select(seed => seed[nameof(EntityKindRow.Code)])
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(EntityKindRegistry.VideoSeason.Code, seededCodes);
        Assert.Contains(EntityKindRegistry.BookVolume.Code, seededCodes);
        Assert.Contains(EntityKindRegistry.BookChapter.Code, seededCodes);
        Assert.Contains(EntityKindRegistry.BookPage.Code, seededCodes);
    }

    [Fact]
    public void EntityChildLinksAreNotPartOfTheCurrentModel() {
        using var db = CreateContext();

        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity =>
            entity.ClrType.Name == "EntityChildLinkRow" ||
            entity.GetTableName() == "entity_child_links");
    }

    [Fact]
    public void EntityRelationshipLinksUseGenericReferenceShape() {
        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(typeof(EntityRelationshipLinkRow));

        Assert.NotNull(modelEntity);
        Assert.Equal("entity_relationship_links", modelEntity!.GetTableName());
        Assert.Null(modelEntity.GetSchema());
        Assert.Contains(modelEntity.GetProperties(), property => property.GetColumnName() == "relationship_code");
        Assert.Contains(modelEntity.GetProperties(), property => property.GetColumnName() == "target_entity_id");
        Assert.Contains(modelEntity.GetProperties(), property => property.GetColumnName() == "target_kind_code");
        Assert.Contains(modelEntity.GetProperties(), property => property.GetColumnName() == "metadata_json");
    }

    [Fact]
    public void EntityRowsExposeNullableParentAndSortOrder() {
        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(typeof(EntityRow));

        var parent = modelEntity!.FindProperty(nameof(EntityRow.ParentEntityId));
        var sortOrder = modelEntity.FindProperty(nameof(EntityRow.SortOrder));

        Assert.NotNull(parent);
        Assert.True(parent!.IsNullable);
        Assert.Equal("parent_entity_id", parent.GetColumnName());
        Assert.NotNull(sortOrder);
        Assert.True(sortOrder!.IsNullable);
        Assert.Equal("sort_order", sortOrder.GetColumnName());

        var parentFk = modelEntity.GetForeignKeys().SingleOrDefault(foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual([nameof(EntityRow.ParentEntityId)]));
        Assert.NotNull(parentFk);
        Assert.Equal(typeof(EntityRow), parentFk!.PrincipalEntityType.ClrType);
    }

    [Theory]
    [InlineData(typeof(BookChapterDetailRow), "book_entity_id")]
    [InlineData(typeof(BookChapterDetailRow), "volume_entity_id")]
    [InlineData(typeof(TagDetailRow), "parent_tag_entity_id")]
    public void DetailRowsDoNotKeepParentSpecificRelationshipColumns(Type entityType, string columnName) {
        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(entityType);

        Assert.NotNull(modelEntity);
        Assert.DoesNotContain(modelEntity!.GetProperties(), property => property.GetColumnName() == columnName);
    }

    [Fact]
    public void VideoSeriesDetailsDoNotStoreRenderingMode() {
        Assert.Null(typeof(VideoSeriesDetailRow).GetProperty("RenderingMode"));

        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(typeof(VideoSeriesDetailRow));

        Assert.NotNull(modelEntity);
        Assert.DoesNotContain(modelEntity!.GetProperties(), property => property.GetColumnName() == "rendering_mode");
    }

    [Fact]
    public void EntityKindsSeedStorageMetadata() {
        using var db = CreateContext();
        var modelEntity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EntityKindRow));

        var videoSeed = modelEntity!.GetSeedData().Single(seed =>
            string.Equals((string)seed[nameof(EntityKindRow.Code)]!, EntityKindRegistry.Video.Code, StringComparison.Ordinal));
        var seriesSeed = modelEntity.GetSeedData().Single(seed =>
            string.Equals((string)seed[nameof(EntityKindRow.Code)]!, EntityKindRegistry.VideoSeries.Code, StringComparison.Ordinal));

        Assert.Equal(EntityStorageShape.File.ToCode(), videoSeed[nameof(EntityKindRow.StorageShape)]);
        Assert.Equal(EntityStorageShape.Folder.ToCode(), seriesSeed[nameof(EntityKindRow.StorageShape)]);
    }

    [Fact]
    public void GalleryDetailsDoNotKeepPhotographerMetadata() {
        Assert.Null(typeof(Gallery).GetProperty("Photographer"));
        Assert.Null(typeof(GalleryDetailRow).GetProperty("Photographer"));

        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(typeof(GalleryDetailRow));

        Assert.NotNull(modelEntity);
        Assert.DoesNotContain(modelEntity!.GetProperties(), property => property.GetColumnName() == "photographer");
    }

    [Fact]
    public void PrismediaMigrationsAreDiscoverableByEfMigrator() {
        using var db = CreateContext();
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();

        Assert.NotEmpty(migrations);
        Assert.EndsWith("InitialPrismediaSchema", migrations[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(EntityRow), nameof(EntityRow.KindCode), "kind_code")]
    [InlineData(typeof(EntityRow), nameof(EntityRow.RatingValue), "rating_value")]
    [InlineData(typeof(EntityDescriptionRow), nameof(EntityDescriptionRow.Value), "value")]
    [InlineData(typeof(EntityStatRow), nameof(EntityStatRow.Code), "code")]
    [InlineData(typeof(EntityTechnicalRow), nameof(EntityTechnicalRow.DurationSeconds), "duration_seconds")]
    [InlineData(typeof(MediaSourceRow), nameof(MediaSourceRow.VideoCodec), "video_codec")]
    [InlineData(typeof(MediaStreamRow), nameof(MediaStreamRow.StreamIndex), "stream_index")]
    [InlineData(typeof(MediaStreamRow), nameof(MediaStreamRow.ColorTransfer), "color_transfer")]
    [InlineData(typeof(MediaStreamRow), nameof(MediaStreamRow.Hdr10PlusPresentFlag), "hdr10_plus_present_flag")]
    [InlineData(typeof(TrickplayInfoRow), nameof(TrickplayInfoRow.TileWidth), "tile_width")]
    [InlineData(typeof(EntitySourceRow), nameof(EntitySourceRow.Value), "value")]
    [InlineData(typeof(EntityProgressRow), nameof(EntityProgressRow.CurrentEntityId), "current_entity_id")]
    [InlineData(typeof(EntityPositionRow), nameof(EntityPositionRow.Label), "label")]
    [InlineData(typeof(EntityClassificationRow), nameof(EntityClassificationRow.Value), "value")]
    [InlineData(typeof(EntityRow), nameof(EntityRow.IsFavorite), "is_favorite")]
    [InlineData(typeof(VideoDetailRow), nameof(VideoDetailRow.SubtitlesExtractedAt), "subtitles_extracted_at")]
    [InlineData(typeof(LibraryRootRow), nameof(LibraryRootRow.ScanVideos), "scan_videos")]
    [InlineData(typeof(AppSettingRow), nameof(AppSettingRow.ValueJson), "value_json")]
    [InlineData(typeof(BrowserSessionRow), nameof(BrowserSessionRow.LastSeenAt), "last_seen_at")]
    [InlineData(typeof(BrowserSessionSettingRow), nameof(BrowserSessionSettingRow.BrowserSessionId), "browser_session_id")]
    [InlineData(typeof(IdentifyQueueItemRow), nameof(IdentifyQueueItemRow.ProviderCode), "provider_code")]
    [InlineData(typeof(DatabaseBackupRow), nameof(DatabaseBackupRow.BackupPath), "backup_path")]
    [InlineData(typeof(JobRunRow), nameof(JobRunRow.AvailableAt), "available_at")]
    public void ModelUsesSnakeCaseColumns(Type entityType, string propertyName, string columnName) {
        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(entityType);

        var property = modelEntity!.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(columnName, property.GetColumnName());
    }

    [Theory]
    [InlineData(typeof(BookDetailRow), nameof(BookDetailRow.BookType), typeof(BookType))]
    [InlineData(typeof(GalleryDetailRow), nameof(GalleryDetailRow.GalleryType), typeof(GalleryType))]
    [InlineData(typeof(CollectionDetailRow), nameof(CollectionDetailRow.Mode), typeof(CollectionMode))]
    [InlineData(typeof(CollectionDetailRow), nameof(CollectionDetailRow.CoverMode), typeof(CollectionCoverMode))]
    [InlineData(typeof(CollectionItemDetailRow), nameof(CollectionItemDetailRow.Source), typeof(CollectionItemSource))]
    [InlineData(typeof(ProviderConfigRow), nameof(ProviderConfigRow.ProviderType), typeof(ProviderType))]
    [InlineData(typeof(IdentifyResultRow), nameof(IdentifyResultRow.Status), typeof(IdentifyResultStatus))]
    [InlineData(typeof(IdentifyQueueItemRow), nameof(IdentifyQueueItemRow.State), typeof(IdentifyQueueState))]
    [InlineData(typeof(FingerprintSubmissionRow), nameof(FingerprintSubmissionRow.Status), typeof(FingerprintSubmissionStatus))]
    [InlineData(typeof(EntityFileRow), nameof(EntityFileRow.Role), typeof(EntityFileRole))]
    [InlineData(typeof(EntitySubtitleRow), nameof(EntitySubtitleRow.Source), typeof(EntitySubtitleSource))]
    [InlineData(typeof(DatabaseBackupRow), nameof(DatabaseBackupRow.Status), typeof(DatabaseBackupStatus))]
    [InlineData(typeof(JobRunRow), nameof(JobRunRow.Type), typeof(JobType))]
    [InlineData(typeof(JobRunRow), nameof(JobRunRow.Status), typeof(JobRunStatus))]
    public void ModelUsesEnumsForClosedChoiceCodes(Type entityType, string propertyName, Type clrType) {
        using var db = CreateContext();
        var modelEntity = db.Model.FindEntityType(entityType);

        var property = modelEntity!.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(clrType, property.ClrType);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseNpgsql("Host=localhost;Database=prismedia;Username=prismedia;Password=prismedia")
            .Options;

        return new PrismediaDbContext(options);
    }
}
