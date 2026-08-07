using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prismedia.Application.Entities;
using Prismedia.Application.Audio;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Identity;
using Prismedia.Application.Jobs.Handlers.Maintenance;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Organization;
using Prismedia.Application.Settings;
using Prismedia.Application.Health;
using Prismedia.Application.Navigation;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;
using Prismedia.Application.Plugins;
using Prismedia.Application.Playback;
using Prismedia.Application.Requests;
using Prismedia.Application.Security;
using Prismedia.Application.Subtitles;

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
        services.AddScoped<IAutomaticSubtitleAcquisitionScheduler, AutomaticSubtitleAcquisitionScheduler>();
        services.AddScoped<NavLayoutService>();
        services.AddScoped<UserAuthService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<CurrentUserContextHolder>();
        services.AddScoped<ICurrentUserContext>(sp => sp.GetRequiredService<CurrentUserContextHolder>());
        services.AddScoped<BrowserSessionService>();
        services.AddScoped<MusicPlayerStateService>();
        services.AddScoped<OrganizeService>();
        services.AddScoped<FilesService>();
        services.AddScoped<EntitySourcePathMutationCoordinator>();
        services.AddScoped<RequestPluginSearchService>();
        services.AddScoped<RequestEntityReviewService>();
        services.AddScoped<IRequestAcquisitionFanoutScheduler, RequestAcquisitionFanoutScheduler>();
        services.AddScoped<RequestCommitService>();
        services.AddScoped<IMonitoredEntityRecovery>(sp => sp.GetRequiredService<RequestCommitService>());
        services.AddScoped<IRequestChildHydrator>(sp => sp.GetRequiredService<RequestCommitService>());
        services.AddScoped<IRequestGraphAcquisitionStarter>(sp => sp.GetRequiredService<RequestCommitService>());
        services.AddScoped<IMissingChildAcquisitionRequester>(sp => sp.GetRequiredService<RequestCommitService>());
        services.AddScoped<Acquisition.IndexerConfigCommandService>();
        services.AddScoped<Acquisition.DownloadClientCommandService>();
        services.AddScoped<Acquisition.BookAcquisitionProfileCommandService>();
        services.AddScoped<Acquisition.IAcquisitionReleaseTimingService, Acquisition.AcquisitionReleaseTimingService>();
        services.AddScoped<Acquisition.IAcquisitionReleaseDateChangeHandler, Acquisition.AcquisitionReleaseDateChangeHandler>();
        services.AddScoped<Acquisition.AcquisitionSearchRunner>();
        services.AddScoped<Acquisition.AcquisitionMissingChildFallback>();
        services.AddSingleton<Acquisition.ManualReplacementSearchSessionStore>();
        services.AddScoped<Acquisition.ManualReplacementService>();
        services.AddScoped<Acquisition.AcquisitionCompletionService>();
        services.AddScoped<Acquisition.AcquisitionUploadService>();
        services.AddScoped<Acquisition.AcquisitionService>();
        services.AddScoped<Acquisition.IAcquisitionRequestService>(sp => sp.GetRequiredService<Acquisition.AcquisitionService>());
        services.AddScoped<Acquisition.IAcquisitionGraphCancellation>(sp => sp.GetRequiredService<Acquisition.AcquisitionService>());
        services.AddScoped<Acquisition.AcquisitionQueueService>();
        services.AddScoped<Acquisition.IAcquisitionQueueService>(sp => sp.GetRequiredService<Acquisition.AcquisitionQueueService>());
        services.AddScoped<Acquisition.MonitorService>();
        services.AddScoped<Acquisition.EntityUnmonitorService>();
        services.AddScoped<Acquisition.IEntityUnmonitorService>(provider =>
            provider.GetRequiredService<Acquisition.EntityUnmonitorService>());
        services.AddScoped<Acquisition.IEntityGiveUpService>(
            provider => provider.GetRequiredService<Acquisition.EntityUnmonitorService>());
        Acquisition.AcquisitionStrategyRegistration.RegisterApplicationStrategies(services);
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
        foreach (var handlerType in JobDefinitionRegistry.All
                     .Select(definition => definition.HandlerType)
                     .Distinct()) {
            services.AddTransient(handlerType);
        }

        // Import execution is worker-only. Policies reuse the concrete scan handlers above so every
        // first-party kind materializes exact placed files before its acquisition reports Imported.
        Acquisition.AcquisitionStrategyRegistration.RegisterWorkerStrategies(services);
        services.AddScoped<IImportedVideoMaterializer, ImportedVideoMaterializer>();
        services.AddScoped<IImportedEntityMaterializer, ImportedEntityMaterializer>();
        services.AddScoped<ImportedTorrentRemover>();
        services.AddScoped<IAcquisitionImportEngineFactory, AcquisitionImportEngineFactory>();

        services.AddTransient<EntityProcessingGraphPlanner>();
        services.AddScoped<EntityLifecycleRecoveryDrainer>();

        services.AddSingleton<AutoIdentifyConcurrencyGate>();

        // Background services
        services.AddSingleton<WorkerRuntimeIdentity>();
        services.AddHostedService<WorkerHeartbeatPublisher>();
        services.AddHostedService<QueueWorker>();
        services.AddHostedService<JobScheduler>();
        services.AddHostedService<JobHistoryPruner>();
        services.AddHostedService<EntityLifecycleRecoveryWorker>();

        return services;
    }
}
