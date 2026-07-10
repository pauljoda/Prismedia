using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Collections;
using Prismedia.Domain.Entities;
using Prismedia.Application.Entities;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Organization;
using Prismedia.Application.Opds;
using Prismedia.Application.Settings;
using Prismedia.Application.Health;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Audio;
using Prismedia.Application.Backups;
using Prismedia.Application.Plugins;
using Prismedia.Application.Playback;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Application.Security;
using Prismedia.Application.Videos;
using Prismedia.Infrastructure.Audio;
using Prismedia.Infrastructure.Collections;
using Prismedia.Infrastructure.Database;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Jobs;
using Prismedia.Infrastructure.Media.Adapters;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Media.Sidecars;
using Prismedia.Infrastructure.Organization;
using Prismedia.Infrastructure.Opds;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Playback;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Queue;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Requests;
using Prismedia.Infrastructure.Settings;
using Prismedia.Infrastructure.Security;
using Prismedia.Infrastructure.StashCompat;
using Prismedia.Infrastructure.Health;
using Prismedia.Infrastructure.Jellyfin;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure;

public static class DependencyInjection {
    public static IServiceCollection AddPrismediaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null) {
        var configuredConnectionString =
            configuration["DATABASE_URL"] ??
            configuration.GetConnectionString("Prismedia") ??
            throw new InvalidOperationException("Prismedia requires DATABASE_URL or ConnectionStrings:Prismedia.");

        var connectionString = PostgresConnectionString.Normalize(configuredConnectionString);
        var pathBase = contentRootPath ?? Directory.GetCurrentDirectory();
        var dataDir = NormalizePath(configuration["PRISMEDIA_DATA_DIR"] ??
            configuration["Prismedia:DataDir"] ??
            "/data", pathBase);
        var cacheDir = NormalizePath(configuration["PRISMEDIA_CACHE_DIR"] ??
            configuration["Prismedia:CacheDir"] ??
            Path.Combine(dataDir, "cache"), pathBase);
        var mediaToolOptions = MediaToolOptions.FromConfiguration(
            configuration["PRISMEDIA_FFMPEG_PATH"] ?? configuration["Prismedia:Hls:FfmpegPath"],
            configuration["PRISMEDIA_FFPROBE_PATH"] ?? configuration["Prismedia:Hls:FfprobePath"]);
        if (int.TryParse(
                configuration["PRISMEDIA_ASSETGEN_THREADS"] ?? configuration["Prismedia:AssetGenerationThreads"],
                out var assetGenThreads) && assetGenThreads >= 1) {
            mediaToolOptions = mediaToolOptions with { AssetGenerationThreads = assetGenThreads };
        }

        RegisterPersistence(services, connectionString);
        RegisterMediaProcessing(services, mediaToolOptions, dataDir, cacheDir);
        RegisterPluginsAndIdentify(services, configuration, pathBase, cacheDir);
        RegisterLibraryScanning(services, dataDir);
        RegisterEntities(services, cacheDir);
        RegisterFilesAndOrganization(services);
        RegisterPlayback(services, configuration, cacheDir, mediaToolOptions);
        RegisterRequests(services);
        RegisterAcquisition(services);
        RegisterJobsSettingsAndState(services, configuration, dataDir, connectionString, pathBase);

        return services;
    }

    private static void RegisterPersistence(IServiceCollection services, string connectionString) {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<PrismediaDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
    }

    private static void RegisterMediaProcessing(
        IServiceCollection services,
        MediaToolOptions mediaToolOptions,
        string dataDir,
        string cacheDir) {
        services.AddSingleton<ProcessExecutor>();
        services.AddSingleton(mediaToolOptions);
        services.AddSingleton<MediaToolService>();
        services.AddSingleton<FileDiscoveryService>();
        services.AddSingleton(new AssetPathService(dataDir, cacheDir));
        services.AddSingleton<EntityAssetCleanupService>();
        services.AddSingleton(provider => new MediaProbeService(
            provider.GetRequiredService<ProcessExecutor>(),
            provider.GetRequiredService<MediaToolOptions>()));
        services.AddSingleton(provider => new ThumbnailService(
            provider.GetRequiredService<ProcessExecutor>(),
            provider.GetRequiredService<MediaProbeService>(),
            provider.GetRequiredService<MediaToolOptions>()));
        services.AddSingleton<HashingService>();
        services.AddSingleton<SkiaImageDownscaler>();
        services.AddSingleton<IImageThumbnailGenerator>(provider =>
            new ImageThumbnailGenerator(
                provider.GetRequiredService<SkiaImageDownscaler>(),
                provider.GetRequiredService<ThumbnailService>()));
        services.AddSingleton<IFileDiscovery>(provider =>
            new FileDiscoveryAdapter(provider.GetRequiredService<FileDiscoveryService>()));
        services.AddSingleton<IMediaProbe>(provider =>
            new MediaProbeAdapter(provider.GetRequiredService<MediaProbeService>()));
        services.AddSingleton<IMediaHashing>(provider =>
            new MediaHashingAdapter(provider.GetRequiredService<HashingService>()));
    }

    private static void RegisterPluginsAndIdentify(
        IServiceCollection services,
        IConfiguration configuration,
        string pathBase,
        string cacheDir) {
        services.AddSingleton(new PluginCatalogOptions(
            ResolvePluginDevPaths(configuration, pathBase),
            cacheDir,
            ResolveCurrentVersion(configuration, pathBase),
            ResolvePluginIndexUrl(configuration),
            ResolveStashScraperIndexUrl(configuration)));
        services.AddSingleton<DotnetPluginProcessRunner>();
        services.AddSingleton<IIdentifyRunner>(provider =>
            provider.GetRequiredService<DotnetPluginProcessRunner>());
        services.AddSingleton<IIdentifyRunner>(provider => new StashCompatRunner(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            new StashScriptExecutor(provider.GetRequiredService<ProcessExecutor>())));
        services.AddSingleton<IdentifyRunnerSelector>();
        services.AddSingleton<PluginIndexCache>();
        services.AddScoped(provider => new PluginCatalogService(
            provider.GetRequiredService<PrismediaDbContext>(),
            provider.GetRequiredService<PluginCatalogOptions>(),
            indexCache: provider.GetRequiredService<PluginIndexCache>()));
        services.AddScoped<IPluginCatalogService>(provider =>
            new ScopedPluginCatalogCache(provider.GetRequiredService<PluginCatalogService>()));
        services.AddScoped<IPluginIdentityRouter, PluginIdentityRouter>();
        services.AddScoped<IPluginIdentityUrlResolver, PluginIdentityUrlResolver>();
        services.AddScoped<IdentifyMatchHintResolver>();
        services.AddScoped(provider => new EntityMetadataApplyService(
            provider.GetRequiredService<PrismediaDbContext>(),
            new PluginArtworkServiceOptions(cacheDir),
            gridThumbnails: provider.GetRequiredService<IGridThumbnailService>(),
            externalIdentities: provider.GetRequiredService<IEntityExternalIdentityStore>(),
            providerIdentities: provider.GetRequiredService<IEntityProviderIdentityStore>(),
            identityRouter: provider.GetRequiredService<IPluginIdentityRouter>(),
            lifecycle: provider.GetRequiredService<IEntityLifecycleMutationLease>()));
        services.AddScoped<IEntityMetadataPatchService>(provider =>
            provider.GetRequiredService<EntityMetadataApplyService>());
        services.AddScoped<IEntityManagementService, EntityManagementService>();
        services.AddScoped<IMediaEntityDeletionService, MediaEntityDeletionService>();
        services.AddScoped<IIdentifyTargetEligibilityService, EfIdentifyTargetEligibilityService>();
        services.AddScoped<IdentifyPluginService>();
        services.AddScoped<IIdentifyProviderService>(provider =>
            provider.GetRequiredService<IdentifyPluginService>());
        services.AddScoped<IdentifyQueueService>();
        services.AddScoped<IIdentifyQueueService>(provider =>
            provider.GetRequiredService<IdentifyQueueService>());
        services.AddScoped<IIdentifySearchRunner, IdentifySearchRunnerAdapter>();
        services.AddScoped<IAutoIdentifyRunner, AutoIdentifyRunner>();
        services.AddScoped<IIdentifyCascadeRunner, IdentifyCascadeRunnerAdapter>();
    }

    private static void RegisterLibraryScanning(IServiceCollection services, string dataDir) {
        services.AddScoped<IMediaAssetGenerator>(provider =>
            new MediaAssetGeneratorAdapter(
                provider.GetRequiredService<ThumbnailService>(),
                provider.GetRequiredService<AssetPathService>(),
                provider.GetRequiredService<SettingsService>(),
                provider.GetRequiredService<MediaToolOptions>()));
        services.AddScoped(provider =>
            new LibraryScanPersistenceService(
                provider.GetRequiredService<PrismediaDbContext>(),
                provider.GetRequiredService<AssetPathService>(),
                provider.GetRequiredService<IEntityLifecycleMutationLease>()));
        services.AddScoped<ILibraryScanRootPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IVideoScanPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IImageGalleryScanPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IAudioScanPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IBookScanPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IDownstreamNeedsPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IImportedEntityReadinessPersistence, EfImportedEntityReadinessPersistence>();
        services.AddScoped<IMediaProcessingStatePersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IEntityRefreshTreePersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IScanMetadataPersistence>(provider =>
            provider.GetRequiredService<LibraryScanPersistenceService>());
        services.AddScoped<IScanSnapshotStore, EfScanSnapshotStore>();
        services.AddSingleton<IVideoSidecarMetadataReader, VideoSidecarMetadataReader>();
        services.AddSingleton<IComicInfoMetadataReader, ComicInfoMetadataReader>();
        services.AddSingleton<IBookFileMetadataReader, Media.Books.BookFileMetadataReader>();
        services.AddSingleton<IBookCoverImageExtractor, Media.Books.BookCoverImageExtractor>();
        services.AddScoped<IMaintenancePersistence>(provider =>
            new MaintenancePersistenceService(provider.GetRequiredService<PrismediaDbContext>(), dataDir));
        services.AddScoped<ICollectionRuleEngine, CollectionRuleEngine>();
        services.AddScoped<ICollectionRefreshPersistence, CollectionRefreshPersistenceService>();
        services.AddScoped<ICollectionItemReadService, CollectionItemReadService>();
        services.AddScoped<ICollectionCommandPersistence, CollectionCommandPersistence>();
        services.AddScoped<ICollectionCommandService, Prismedia.Application.Collections.CollectionCommandService>();
    }

    private static void RegisterEntities(IServiceCollection services, string cacheDir) {
        RegisterEntityMappers(services);
        RegisterThumbnailContributors(services);
        services.AddScoped<IEntityExternalIdentityStore, EfEntityExternalIdentityStore>();
        services.AddScoped<IEntityProviderIdentityStore, EfEntityProviderIdentityStore>();
        services.AddScoped<IEntityHierarchyReader, EfEntityHierarchyReader>();
        services.AddScoped<IEntityLifecycleMutationLease, EfEntityLifecycleMutationLease>();
        services.AddScoped<EfEntitySourceOwnershipProjection>();
        services.AddScoped<IEntitySourceOwnershipReader>(provider =>
            provider.GetRequiredService<EfEntitySourceOwnershipProjection>());
        services.AddScoped<IEntityFileDeletionRecoveryReader, EfEntityFileDeletionRecoveryProjection>();
        services.AddScoped<EfEntityRepository>();
        services.AddScoped<IEntityWriteRepository>(provider => provider.GetRequiredService<EfEntityRepository>());
        // Concrete registration shared by the interface alias and the visibility checker so all
        // three see the same per-request hidden-roots memoization.
        services.AddScoped<EfEntityReadService>();
        services.AddScoped<IEntityReadService>(provider => provider.GetRequiredService<EfEntityReadService>());
        services.AddScoped<IEntityVisibilityChecker, EfEntityVisibilityChecker>();
        services.AddScoped<EfLibraryAccessReader>();
        services.AddScoped<ILibraryAccessReader>(provider => provider.GetRequiredService<EfLibraryAccessReader>());
        services.AddScoped<ILibraryAccessStore>(provider => provider.GetRequiredService<EfLibraryAccessReader>());
        services.AddScoped<IEntityFileContentService, EfEntityFileContentService>();
        services.AddScoped<IGridThumbnailService>(provider =>
            new GridThumbnailService(
                provider.GetRequiredService<PrismediaDbContext>(),
                provider.GetRequiredService<AssetPathService>(),
                provider.GetRequiredService<IImageThumbnailGenerator>()));
        services.AddScoped<IEntityImageAssetMutationService>(provider =>
            new EntityImageAssetMutationService(
                provider.GetRequiredService<PrismediaDbContext>(),
                new EntityImageAssetStorageOptions(cacheDir),
                provider.GetRequiredService<IGridThumbnailService>()));
        services.AddScoped<IOpdsCatalogService, EfOpdsCatalogService>();
    }

    private static void RegisterFilesAndOrganization(IServiceCollection services) {
        services.AddScoped<IOrganizePersistence, EfOrganizePersistence>();
        services.AddScoped<IFilesPersistence, EfFilesPersistence>();
        services.AddScoped<IEntitySourcePathOwnerReader, EfEntitySourcePathOwnerReader>();
        services.AddSingleton<IManagedFileStorage, LocalManagedFileStorage>();
    }

    private static void RegisterPlayback(
        IServiceCollection services,
        IConfiguration configuration,
        string cacheDir,
        MediaToolOptions mediaToolOptions) {
        services.AddScoped<IVideoSourceService, VideoSourceService>();
        services.AddScoped<IAudioSourceService, AudioSourceService>();
        services.AddSingleton<IAudioTranscodeOptions, MediaToolAudioTranscodeOptions>();
        services.AddSingleton(new HlsAssetServiceOptions(
            cacheDir,
            HlsTranscoderProfiles.ParseOrDefault(configuration["PRISMEDIA_HLS_TRANSCODER"] ?? configuration["Prismedia:Hls:Transcoder"]),
            mediaToolOptions.FfmpegPath,
            configuration["PRISMEDIA_VAAPI_DEVICE"] ?? configuration["Prismedia:Hls:VaapiDevice"] ?? "/dev/dri/renderD128",
            mediaToolOptions.FfprobePath));
        services.AddSingleton<ITranscodeSessionService, TranscodeSessionService>();
        services.AddSingleton<ITranscodeCacheService, TranscodeCacheService>();
        services.AddScoped<IHlsAssetService, HlsAssetService>();
        services.AddScoped<IPlaybackInfoService, PlaybackInfoService>();
        services.AddScoped<IPlaybackSessionService, PlaybackSessionService>();
        services.AddScoped<IPlaybackEventStore, EfPlaybackEventStore>();
        services.AddScoped<IPlaybackStatisticsService, EfPlaybackStatisticsService>();
        services.AddScoped<ITrickplayService, TrickplayService>();
        services.AddScoped<IVideoSubtitleAssetService>(provider =>
            new VideoSubtitleAssetService(
                provider.GetRequiredService<PrismediaDbContext>(),
                provider.GetRequiredService<ProcessExecutor>(),
                provider.GetRequiredService<MediaToolOptions>()));
    }

    private static void RegisterJobsSettingsAndState(
        IServiceCollection services,
        IConfiguration configuration,
        string dataDir,
        string connectionString,
        string pathBase) {
        var backupDir = NormalizePath(configuration["PRISMEDIA_BACKUP_DIR"] ??
            configuration["Prismedia:Backups:Directory"] ??
            Path.Combine(dataDir, "backups", "database"), dataDir);
        var retentionDays = int.TryParse(
                configuration["PRISMEDIA_BACKUP_RETENTION_DAYS"] ??
                configuration["Prismedia:Backups:RetentionDays"],
                out var configuredRetentionDays) && configuredRetentionDays > 0
            ? configuredRetentionDays
            : 7;
        var repoRoot = FindRepoRoot(pathBase);
        var dockerComposeFilePath = configuration["PRISMEDIA_BACKUP_DOCKER_COMPOSE_FILE"] ??
            configuration["Prismedia:Backups:DockerComposeFile"] ??
            (repoRoot is null ? null : Path.Combine(repoRoot, "infra", "docker", "docker-compose.yml"));
        if (!string.IsNullOrWhiteSpace(dockerComposeFilePath)) {
            dockerComposeFilePath = NormalizePath(dockerComposeFilePath, pathBase);
        }

        services.AddSingleton(new DatabaseBackupOptions(
            connectionString,
            backupDir,
            Path.Combine(backupDir, "restore-request.json"),
            configuration["PRISMEDIA_PG_DUMP_PATH"] ??
            configuration["Prismedia:Backups:PgDumpPath"] ??
            "pg_dump",
            configuration["PRISMEDIA_PG_RESTORE_PATH"] ??
            configuration["Prismedia:Backups:PgRestorePath"] ??
            "pg_restore",
            dockerComposeFilePath,
            configuration["PRISMEDIA_BACKUP_DOCKER_COMPOSE_POSTGRES_SERVICE"] ??
            configuration["Prismedia:Backups:DockerComposePostgresService"] ??
            "postgres",
            retentionDays,
            TimeSpan.FromDays(1)));
        services.AddSingleton<IWorkerHeartbeatStore>(new FileWorkerHeartbeatStore(dataDir));
        services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
        services.AddScoped<IJobQueueService, JobQueueService>();
        services.AddScoped<ISettingsPersistence, EfSettingsPersistence>();
        services.AddScoped<IBrowserSessionPersistence, EfBrowserSessionPersistence>();
        services.AddScoped<ISecurityPersistence, EfSecurityPersistence>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddScoped<IUserEngagementCloner, EfUserEngagementCloner>();
        services.AddScoped<IJellyfinImageFileService, JellyfinImageFileService>();
    }

    private static void RegisterRequests(IServiceCollection services) {
        // Prismedia fulfils all requests itself through its plugin-backed acquisition pipeline; the
        // request layer is the kind-registry-driven selected-plugin search + proposal review that feeds
        // acquisitions. One source class serves every requestable kind.
        services.AddScoped<IPluginRequestSearchSource, PluginRequestMetadataSource>();
        services.AddScoped<IRequestMetadataEnricher, PluginRequestMetadataSource>();
        services.AddScoped<IPluginRequestReviewSource, PluginRequestMetadataSource>();
        services.AddScoped<IPluginRequestProposalSource, PluginRequestMetadataSource>();
        services.AddScoped<IWantedEntityWriter, WantedEntityWriter>();
        services.AddScoped<IWantedSuppressionStore, EfWantedSuppressionStore>();
        services.AddScoped<IProviderTrackingCatalog, PluginProviderTrackingCatalog>();
    }

    private static void RegisterAcquisition(IServiceCollection services) {
        services.AddScoped<IIndexerConfigStore, EfIndexerConfigStore>();
        services.AddScoped<IIndexerStatusStore, EfIndexerStatusStore>();
        services.AddSingleton<IndexerQueryWindow>();
        services.AddScoped<IRemotePathMappingStore, EfRemotePathMappingStore>();
        services.AddScoped<RemotePathMapper>();
        services.AddScoped<IRecycleBin, RecycleBin>();
        services.AddScoped<IDownloadClientConfigStore, EfDownloadClientConfigStore>();
        services.AddScoped<IBookAcquisitionProfileStore, EfBookAcquisitionProfileStore>();
        services.AddScoped<ICustomFormatStore, EfCustomFormatStore>();
        services.AddScoped<IAcquisitionStore, EfAcquisitionStore>();
        services.AddScoped<IAcquisitionLifecycleStore>(provider =>
            provider.GetRequiredService<IAcquisitionStore>());
        services.AddScoped<IAcquisitionTransferAddCoordinator, EfAcquisitionTransferAddCoordinator>();
        services.AddScoped<IAcquisitionBlocklistStore, EfAcquisitionBlocklistStore>();
        services.AddScoped<IAcquisitionHistoryStore, EfAcquisitionHistoryStore>();
        services.AddScoped<IMonitorStore, EfMonitorStore>();
        services.AddScoped<IAcquisitionJobCleanup, AcquisitionJobCleanup>();
        services.AddScoped<IEntityUnmonitorPersistence, EfEntityUnmonitorPersistence>();
        // Explicit indexer timeouts: a hung indexer must fail the search (which surfaces per-indexer in
        // the outcome) rather than pin the whole search job. Prowlarr gets longer because one call fans
        // out across every indexer it aggregates; direct Torznab/Newznab calls hit a single tracker.
        services.AddScoped(_ => new ProwlarrIndexerClient(new HttpClient { Timeout = TimeSpan.FromSeconds(100) }));
        services.AddScoped<IIndexerSearchClient>(provider => provider.GetRequiredService<ProwlarrIndexerClient>());
        services.AddScoped(_ => new TorznabIndexerClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }));
        services.AddScoped<IIndexerSearchClient>(provider => provider.GetRequiredService<TorznabIndexerClient>());
        services.AddScoped(_ => new NewznabIndexerClient(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }));
        services.AddScoped<IIndexerSearchClient>(provider => provider.GetRequiredService<NewznabIndexerClient>());
        services.AddScoped<IIndexerSearchClientFactory, IndexerSearchClientFactory>();
        // UseCookies=false keeps the default handler from swallowing qBittorrent's Set-Cookie SID,
        // so the client can read and re-send the session cookie explicitly.
        services.AddScoped(_ => new QBittorrentDownloadClient(new HttpClient(new HttpClientHandler { UseCookies = false })));
        services.AddScoped<IDownloadClient>(provider => provider.GetRequiredService<QBittorrentDownloadClient>());
        services.AddScoped(_ => new SabnzbdDownloadClient(new HttpClient()));
        services.AddScoped<IDownloadClient>(provider => provider.GetRequiredService<SabnzbdDownloadClient>());
        services.AddScoped(_ => new TransmissionDownloadClient(new HttpClient()));
        services.AddScoped<IDownloadClient>(provider => provider.GetRequiredService<TransmissionDownloadClient>());
        services.AddScoped<IDownloadClientFactory, DownloadClientFactory>();
        services.AddScoped<IAcquisitionImportPlanner, AcquisitionImportPlanner>();
        services.AddScoped<IImportFileMover, ImportFileMover>();
        services.AddScoped<IOwnedFileReplacer, OwnedFileReplacer>();
        services.AddScoped<IAcquisitionHintApplier>(provider => new AcquisitionHintApplier(
            provider.GetRequiredService<PrismediaDbContext>(),
            provider.GetRequiredService<IEntityExternalIdentityStore>(),
            provider.GetRequiredService<IEntityLifecycleMutationLease>()));
        services.AddScoped<IImportTargetIndex, EfImportTargetIndex>();
        services.AddScoped<IImportedFilesReader, ImportedFilesReader>();
        services.AddScoped<IDownloadPayloadReader, DownloadPayloadReader>();
        // No auto-redirect: the resolver validates the destination host is public before fetching, and a
        // redirect could hop to a private address that bypasses that check (SSRF defense for the LAN host).
        services.AddScoped<IReleaseLinkResolver>(_ => new ReleaseLinkResolver(
            new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) }));
    }

    private static void RegisterThumbnailContributors(IServiceCollection services) {
        var contributorType = typeof(Entities.Thumbnails.IThumbnailContributor);
        foreach (var type in contributorType.Assembly.GetTypes()
                     .Where(type => type is { IsClass: true, IsAbstract: false } &&
                                    contributorType.IsAssignableFrom(type))) {
            services.AddScoped(contributorType, type);
        }
    }

    private static void RegisterEntityMappers(IServiceCollection services) {
        var assembly = typeof(IEntityKindMapper).Assembly;
        var implementations = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false });

        var explicitKindTypes = implementations
            .Where(type => typeof(IEntityKindMapper).IsAssignableFrom(type) &&
                           type != typeof(EntityMappers.ConventionEntityKindMapper))
            .ToArray();
        foreach (var type in explicitKindTypes) {
            services.AddScoped(typeof(IEntityKindMapper), type);
        }

        var explicitKindNames = explicitKindTypes
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var descriptor in EntityKindRegistry.All.Where(descriptor =>
                     descriptor.ClrType is not null &&
                     !explicitKindNames.Contains($"{descriptor.Value}KindMapper"))) {
            services.AddScoped<IEntityKindMapper>(_ => new EntityMappers.ConventionEntityKindMapper(descriptor));
        }

        foreach (var type in implementations.Where(type => typeof(IEntityCapabilityMapper).IsAssignableFrom(type))) {
            services.AddScoped(typeof(IEntityCapabilityMapper), type);
        }
    }

    private static string NormalizePath(string path, string basePath) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(basePath, path));

    private static IReadOnlyList<string> ResolvePluginDevPaths(IConfiguration configuration, string basePath) {
        var configured = configuration["PRISMEDIA_PLUGIN_DEV_PATHS"] ??
            configuration["Prismedia:Plugins:DevPaths"];
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) {
            paths.AddRange(configured
                .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(path => NormalizePath(path, basePath)));
        }

        var repoRoot = FindRepoRoot(basePath);
        if (repoRoot is not null) {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home)) {
                var prismediaPluginsRepo = Path.Combine(home, "Dev", "Prismedia-Plugins");
                if (Directory.Exists(prismediaPluginsRepo)) {
                    paths.Add(prismediaPluginsRepo);
                }
            }

            paths.Add(Path.Combine(repoRoot, "packages", "plugins"));
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveCurrentVersion(IConfiguration configuration, string basePath) {
        var configured = configuration["PRISMEDIA_VERSION"] ??
            configuration["Prismedia:Version"];
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured;
        }

        var repoRoot = FindRepoRoot(basePath);
        if (repoRoot is null) {
            return "1.0.0";
        }

        var packageJson = Path.Combine(repoRoot, "package.json");
        if (!File.Exists(packageJson)) {
            return "1.0.0";
        }

        try {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packageJson));
            return document.RootElement.TryGetProperty("version", out var version)
                ? version.GetString() ?? "1.0.0"
                : "1.0.0";
        } catch (System.Text.Json.JsonException) {
            return "1.0.0";
        } catch (IOException) {
            return "1.0.0";
        }
    }

    private static string? ResolvePluginIndexUrl(IConfiguration configuration) {
        var configured = configuration["PRISMEDIA_PLUGIN_INDEX_URL"] ??
            configuration["Prismedia:Plugins:IndexUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured;
        }

        if (string.Equals(
                configuration["PRISMEDIA_PLUGIN_INDEX_DISABLED"] ??
                configuration["Prismedia:Plugins:IndexDisabled"],
                "true",
                StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return "https://raw.githubusercontent.com/pauljoda/Prismedia-Plugins/main/index.yml";
    }

    private static string? ResolveStashScraperIndexUrl(IConfiguration configuration) {
        var configured = configuration["PRISMEDIA_STASH_SCRAPER_INDEX_URL"] ??
            configuration["Prismedia:StashScrapers:IndexUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured;
        }

        if (string.Equals(
                configuration["PRISMEDIA_STASH_SCRAPERS_DISABLED"] ??
                configuration["Prismedia:StashScrapers:Disabled"],
                "true",
                StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return "https://stashapp.github.io/CommunityScrapers/stable/index.yml";
    }

    private static string? FindRepoRoot(string start) {
        var directory = new DirectoryInfo(start);
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "pnpm-workspace.yaml")) &&
                File.Exists(Path.Combine(directory.FullName, "package.json"))) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

}
