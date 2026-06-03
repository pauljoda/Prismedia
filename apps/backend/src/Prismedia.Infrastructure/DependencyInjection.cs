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
using Prismedia.Application.Settings;
using Prismedia.Application.Health;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Audio;
using Prismedia.Application.Plugins;
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
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Queue;
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
        RegisterMediaProcessing(services, mediaToolOptions, dataDir);
        RegisterPluginsAndIdentify(services, configuration, pathBase, cacheDir);
        RegisterLibraryScanning(services, dataDir);
        RegisterEntities(services, cacheDir);
        RegisterFilesAndOrganization(services);
        RegisterPlayback(services, configuration, cacheDir, mediaToolOptions);
        RegisterJobsSettingsAndState(services, dataDir);

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
        string dataDir) {
        services.AddSingleton<ProcessExecutor>();
        services.AddSingleton(mediaToolOptions);
        services.AddSingleton<MediaToolService>();
        services.AddSingleton<FileDiscoveryService>();
        services.AddSingleton(new AssetPathService(dataDir));
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
        services.AddScoped(provider => new PluginCatalogService(
            provider.GetRequiredService<PrismediaDbContext>(),
            provider.GetRequiredService<PluginCatalogOptions>()));
        services.AddScoped<IPluginCatalogService>(provider =>
            provider.GetRequiredService<PluginCatalogService>());
        services.AddScoped<IdentifyMatchHintResolver>();
        services.AddScoped(provider => new EntityMetadataApplyService(
            provider.GetRequiredService<PrismediaDbContext>(),
            new PluginArtworkServiceOptions(cacheDir)));
        services.AddScoped<IEntityMetadataPatchService>(provider =>
            provider.GetRequiredService<EntityMetadataApplyService>());
        services.AddScoped<IEntityManagementService, EntityManagementService>();
        services.AddScoped<IdentifyPluginService>();
        services.AddScoped<IIdentifyProviderService>(provider =>
            provider.GetRequiredService<IdentifyPluginService>());
        services.AddScoped<IdentifyQueueService>();
        services.AddScoped<IIdentifyQueueService>(provider =>
            provider.GetRequiredService<IdentifyQueueService>());
        services.AddScoped<IBulkIdentifyProvider, BulkIdentifyProviderAdapter>();
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
        services.AddScoped<LibraryScanPersistenceService>();
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
        services.AddScoped<EfEntityRepository>();
        services.AddScoped<IEntityWriteRepository>(provider => provider.GetRequiredService<EfEntityRepository>());
        services.AddScoped<IEntityReadService, EfEntityReadService>();
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
    }

    private static void RegisterFilesAndOrganization(IServiceCollection services) {
        services.AddScoped<IOrganizePersistence, EfOrganizePersistence>();
        services.AddScoped<IFilesPersistence, EfFilesPersistence>();
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
        services.AddScoped<IHlsAssetService, HlsAssetService>();
        services.AddScoped<IPlaybackInfoService, PlaybackInfoService>();
        services.AddScoped<IPlaybackSessionService, PlaybackSessionService>();
        services.AddScoped<ITrickplayService, TrickplayService>();
        services.AddScoped<IVideoSubtitleAssetService>(provider =>
            new VideoSubtitleAssetService(
                provider.GetRequiredService<PrismediaDbContext>(),
                provider.GetRequiredService<ProcessExecutor>(),
                provider.GetRequiredService<MediaToolOptions>()));
    }

    private static void RegisterJobsSettingsAndState(IServiceCollection services, string dataDir) {
        services.AddSingleton<IWorkerHeartbeatStore>(new FileWorkerHeartbeatStore(dataDir));
        services.AddScoped<IJobQueueService, JobQueueService>();
        services.AddScoped<ISettingsPersistence, EfSettingsPersistence>();
        services.AddScoped<ISecurityPersistence, EfSecurityPersistence>();
        services.AddScoped<IJellyfinImageFileService, JellyfinImageFileService>();
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
