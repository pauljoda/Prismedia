using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Entities;
using Prismedia.Application.Audio;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Organization;
using Prismedia.Application.Settings;
using Prismedia.Application.Health;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Navigation;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Generate;
using Prismedia.Application.Jobs.Handlers.Identity;
using Prismedia.Application.Jobs.Handlers.Import;
using Prismedia.Application.Jobs.Handlers.Maintenance;
using Prismedia.Application.Jobs.Handlers.Probe;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Playback;
using Prismedia.Application.Requests;
using Prismedia.Application.Security;

namespace Prismedia.Application;

/// <summary>
/// Registers application-layer use-case services.
/// </summary>
public static class DependencyInjection {
    /// <summary>
    /// Adds Prismedia application services that orchestrate domain ports for API endpoints and future workers.
    /// </summary>
    public static IServiceCollection AddPrismediaApplication(this IServiceCollection services) {
        services.AddScoped<JobService>();
        services.AddScoped<EntityCapabilityService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SettingsService>();
        services.AddScoped<NavLayoutService>();
        services.AddScoped<UserAuthService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<CurrentUserContextHolder>();
        services.AddScoped<ICurrentUserContext>(sp => sp.GetRequiredService<CurrentUserContextHolder>());
        services.AddScoped<BrowserSessionService>();
        services.AddScoped<MusicPlayerStateService>();
        services.AddSingleton<IJellyfinAudioPlaybackTracker, JellyfinAudioPlaybackTracker>();
        services.AddScoped<JellyfinCatalogService>();
        services.AddScoped<OrganizeService>();
        services.AddScoped<FilesService>();
        services.AddScoped<EntitySourcePathMutationCoordinator>();
        services.AddScoped<RequestPluginSearchService>();
        services.AddScoped<RequestEntityReviewService>();
        services.AddScoped<RequestCommitService>();
        services.AddScoped<IMonitoredEntityRecovery>(sp => sp.GetRequiredService<RequestCommitService>());
        services.AddScoped<Acquisition.IndexerConfigCommandService>();
        services.AddScoped<Acquisition.DownloadClientCommandService>();
        services.AddScoped<Acquisition.BookAcquisitionProfileCommandService>();
        services.AddScoped<Acquisition.AcquisitionSearchRunner>();
        services.AddScoped<Acquisition.AcquisitionService>();
        services.AddScoped<Acquisition.IAcquisitionRequestService>(sp => sp.GetRequiredService<Acquisition.AcquisitionService>());
        services.AddScoped<Acquisition.AcquisitionQueueService>();
        services.AddScoped<Acquisition.IAcquisitionQueueService>(sp => sp.GetRequiredService<Acquisition.AcquisitionQueueService>());
        services.AddScoped<Acquisition.MonitorService>();
        services.AddScoped<Acquisition.EntityUnmonitorService>();
        services.AddScoped<Acquisition.IEntityGiveUpService>(
            provider => provider.GetRequiredService<Acquisition.EntityUnmonitorService>());
        services.AddSingleton<Acquisition.IAcquisitionPolicyModule, Acquisition.BookAcquisitionPolicyModule>();
        services.AddSingleton<Acquisition.IAcquisitionPolicyModule, Acquisition.MovieAcquisitionPolicyModule>();
        services.AddSingleton<Acquisition.IAcquisitionPolicyModule, Acquisition.MusicAcquisitionPolicyModule>();
        services.AddSingleton<Acquisition.IAcquisitionPolicyModule, Acquisition.TvAcquisitionPolicyModule>();
        services.AddSingleton<Acquisition.IAcquisitionPolicyRegistry, Acquisition.AcquisitionPolicyRegistry>();
        services.AddSingleton<VideoScanConcurrencyGate>();
        services.AddScoped<IAudioStreamService, AudioStreamService>();
        services.AddSingleton<IIdentifyApplyProgressStore, InMemoryIdentifyApplyProgressStore>();
        services.AddSingleton<AuthAttemptThrottle>();

        return services;
    }

    /// <summary>
    /// Adds application job handlers, the hosted queue worker, scan scheduler, and history pruner.
    /// </summary>
    public static IServiceCollection AddPrismediaWorkerApplication(this IServiceCollection services) {
        // Utility handlers
        services.AddTransient<IJobHandler, NoOpJobHandler>();

        // Scanning
        services.AddTransient<ScanLibraryJobHandler>();
        services.AddTransient<IJobHandler>(provider => provider.GetRequiredService<ScanLibraryJobHandler>());
        services.AddTransient<IJobHandler, ScanGalleryJobHandler>();
        services.AddTransient<ScanBookJobHandler>();
        services.AddTransient<IJobHandler>(provider => provider.GetRequiredService<ScanBookJobHandler>());
        services.AddTransient<ScanAudioJobHandler>();
        services.AddTransient<IJobHandler>(provider => provider.GetRequiredService<ScanAudioJobHandler>());

        // Import execution is worker-only. Policies reuse the concrete scan handlers above so every
        // first-party kind materializes exact placed files before its acquisition reports Imported.
        services.AddScoped<IImportedVideoMaterializer, ImportedVideoMaterializer>();
        services.AddScoped<IImportedEntityMaterializationPolicy, ImportedBookMaterializationPolicy>();
        services.AddScoped<IImportedEntityMaterializationPolicy, ImportedMovieMaterializationPolicy>();
        services.AddScoped<IImportedEntityMaterializationPolicy, ImportedAlbumMaterializationPolicy>();
        services.AddScoped<IImportedEntityMaterializer, ImportedEntityMaterializer>();
        services.AddScoped<ImportedTorrentRemover>();
        services.AddScoped<IAcquisitionImportEngine, BookAcquisitionImportEngine>();
        services.AddScoped<IAcquisitionImportEngine, MovieAcquisitionImportEngine>();
        services.AddScoped<IAcquisitionImportEngine, MusicAcquisitionImportEngine>();
        // One TV engine class serves both acquisition units: season packs and single episodes.
        services.AddScoped<IAcquisitionImportEngine>(provider =>
            ActivatorUtilities.CreateInstance<TvAcquisitionImportEngine>(provider, EntityKind.VideoSeason));
        services.AddScoped<IAcquisitionImportEngine>(provider =>
            ActivatorUtilities.CreateInstance<TvAcquisitionImportEngine>(provider, EntityKind.Video));
        services.AddScoped<IAcquisitionImportEngineFactory, AcquisitionImportEngineFactory>();

        // Probing
        services.AddTransient<IJobHandler, ProbeVideoJobHandler>();
        services.AddTransient<IJobHandler, ProbeAudioJobHandler>();

        // Fingerprinting (single handler, registered per job type)
        services.AddTransient<IJobHandler>(sp => new FingerprintJobHandler(
            JobType.FingerprintVideo,
            sp.GetRequiredService<ILogger<FingerprintJobHandler>>(),
            sp.GetRequiredService<IMediaHashing>(),
            sp.GetRequiredService<ILibraryScanRootPersistence>(),
            sp.GetRequiredService<IMediaProcessingStatePersistence>()));
        services.AddTransient<IJobHandler>(sp => new FingerprintJobHandler(
            JobType.FingerprintImage,
            sp.GetRequiredService<ILogger<FingerprintJobHandler>>(),
            sp.GetRequiredService<IMediaHashing>(),
            sp.GetRequiredService<ILibraryScanRootPersistence>(),
            sp.GetRequiredService<IMediaProcessingStatePersistence>()));
        services.AddTransient<IJobHandler>(sp => new FingerprintJobHandler(
            JobType.FingerprintAudio,
            sp.GetRequiredService<ILogger<FingerprintJobHandler>>(),
            sp.GetRequiredService<IMediaHashing>(),
            sp.GetRequiredService<ILibraryScanRootPersistence>(),
            sp.GetRequiredService<IMediaProcessingStatePersistence>()));

        // Preview / asset generation
        services.AddTransient<IJobHandler, GeneratePreviewJobHandler>();
        services.AddTransient<IJobHandler, GenerateGridThumbnailJobHandler>();
        services.AddTransient<IJobHandler, GridThumbnailSweepJobHandler>();
        services.AddTransient<IJobHandler, GenerateImageThumbnailJobHandler>();
        services.AddTransient<IJobHandler, GenerateBookPageThumbnailJobHandler>();
        services.AddTransient<IJobHandler, GenerateBookCoverThumbnailJobHandler>();
        services.AddTransient<IJobHandler, GenerateAudioWaveformJobHandler>();
        services.AddTransient<IJobHandler, ExtractSubtitlesJobHandler>();

        // Metadata / collections / maintenance
        services.AddTransient<IJobHandler, ImportMetadataJobHandler>();
        services.AddTransient<IJobHandler, RefreshCollectionJobHandler>();
        services.AddTransient<IJobHandler, RefreshEntityJobHandler>();
        services.AddTransient<IJobHandler, LibraryMaintenanceJobHandler>();
        services.AddTransient<IJobHandler, DatabaseBackupJobHandler>();

        // Identify
        services.AddSingleton<AutoIdentifyConcurrencyGate>();
        services.AddTransient<IJobHandler, IdentifySearchJobHandler>();
        services.AddTransient<IJobHandler, BulkIdentifyJobHandler>();
        services.AddTransient<IJobHandler, AutoIdentifyJobHandler>();
        services.AddTransient<IJobHandler, IdentifyCascadeJobHandler>();

        // Acquisition
        services.AddTransient<IJobHandler, AcquisitionSearchJobHandler>();
        services.AddTransient<IJobHandler, AcquisitionMonitorJobHandler>();
        services.AddTransient<IJobHandler, AcquisitionImportJobHandler>();
        services.AddTransient<IJobHandler, RecycleBinCleanupJobHandler>();
        services.AddTransient<IJobHandler, AcquisitionFailedHandleJobHandler>();
        services.AddTransient<IJobHandler, MonitoredSearchJobHandler>();
        services.AddTransient<IJobHandler, AcquisitionUpgradeReplaceJobHandler>();
        services.AddTransient<IJobHandler, AcquisitionEnrichJobHandler>();

        // Background services
        services.AddSingleton<WorkerRuntimeIdentity>();
        services.AddHostedService<WorkerHeartbeatPublisher>();
        services.AddHostedService<QueueWorker>();
        services.AddHostedService<JobScheduler>();
        services.AddHostedService<JobHistoryPruner>();

        return services;
    }
}
