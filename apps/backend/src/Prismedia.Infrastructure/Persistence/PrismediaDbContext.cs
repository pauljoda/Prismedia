using Microsoft.EntityFrameworkCore;
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

    public DbSet<MusicArtistDetailRow> MusicArtistDetails => Set<MusicArtistDetailRow>();

    public DbSet<AudioLibraryDetailRow> AudioLibraryDetails => Set<AudioLibraryDetailRow>();

    public DbSet<AudioTrackDetailRow> AudioTrackDetails => Set<AudioTrackDetailRow>();

    public DbSet<PersonDetailRow> PersonDetails => Set<PersonDetailRow>();

    public DbSet<TagDetailRow> TagDetails => Set<TagDetailRow>();

    public DbSet<CollectionDetailRow> CollectionDetails => Set<CollectionDetailRow>();

    public DbSet<CollectionItemDetailRow> CollectionItemDetails => Set<CollectionItemDetailRow>();

    public DbSet<LibraryRootRow> LibraryRoots => Set<LibraryRootRow>();

    public DbSet<MediaFileIgnoreRow> MediaFileIgnores => Set<MediaFileIgnoreRow>();

    public DbSet<ScannedFileRow> ScannedFiles => Set<ScannedFileRow>();

    public DbSet<AppSettingRow> AppSettings => Set<AppSettingRow>();

    public DbSet<AppSecurityRow> AppSecurity => Set<AppSecurityRow>();

    public DbSet<JellyfinProfileRow> JellyfinProfiles => Set<JellyfinProfileRow>();

    public DbSet<JellyfinSessionRow> JellyfinSessions => Set<JellyfinSessionRow>();

    public DbSet<UiPreferenceRow> UiPreferences => Set<UiPreferenceRow>();

    public DbSet<BrowserSessionRow> BrowserSessions => Set<BrowserSessionRow>();

    public DbSet<BrowserSessionSettingRow> BrowserSessionSettings => Set<BrowserSessionSettingRow>();

    public DbSet<ProviderConfigRow> ProviderConfigs => Set<ProviderConfigRow>();

    public DbSet<ProviderCredentialRow> ProviderCredentials => Set<ProviderCredentialRow>();

    public DbSet<RequestServiceInstanceRow> RequestServiceInstances => Set<RequestServiceInstanceRow>();

    public DbSet<RequestServiceCredentialRow> RequestServiceCredentials => Set<RequestServiceCredentialRow>();

    public DbSet<RequestHistoryRow> RequestHistory => Set<RequestHistoryRow>();

    public DbSet<IdentifyResultRow> IdentifyResults => Set<IdentifyResultRow>();

    public DbSet<IdentifyQueueItemRow> IdentifyQueueItems => Set<IdentifyQueueItemRow>();

    public DbSet<FingerprintSubmissionRow> FingerprintSubmissions => Set<FingerprintSubmissionRow>();

    public DbSet<DatabaseBackupRow> DatabaseBackups => Set<DatabaseBackupRow>();

    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.ConfigureBaseEntityModel();
        modelBuilder.ConfigureEntityRelationshipModel();
        modelBuilder.ConfigureEntityAttachmentModel();
        modelBuilder.ConfigurePrismediaModel();

        // job_runs is mutated concurrently by background worker tasks (claim/progress/complete/fail)
        // and by API endpoints (cancel), so it uses the PostgreSQL xmin system column as an optimistic
        // concurrency token to detect and resolve conflicting writes. xmin already exists on every row,
        // so this maps to it without DDL. Guarded to Npgsql so the in-memory test provider is unaffected.
        if (Database.IsNpgsql()) {
            // A uint, store-generated, concurrency-token property is recognized by the Npgsql convention
            // and mapped to the existing xmin system column without emitting any migration DDL.
            modelBuilder.Entity<JobRunRow>()
                .Property<uint>("Version")
                .IsRowVersion();
        }
    }
}
