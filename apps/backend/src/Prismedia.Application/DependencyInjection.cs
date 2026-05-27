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
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Generate;
using Prismedia.Application.Jobs.Handlers.Identity;
using Prismedia.Application.Jobs.Handlers.Import;
using Prismedia.Application.Jobs.Handlers.Maintenance;
using Prismedia.Application.Jobs.Handlers.Probe;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

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
        services.AddScoped<SettingsService>();
        services.AddScoped<OrganizeService>();
        services.AddScoped<FilesService>();
        services.AddScoped<IAudioStreamService, AudioStreamService>();

        return services;
    }

    /// <summary>
    /// Adds application job handlers, the hosted queue worker, scan scheduler, and history pruner.
    /// </summary>
    public static IServiceCollection AddPrismediaWorkerApplication(this IServiceCollection services) {
        // Utility handlers
        services.AddTransient<IJobHandler, NoOpJobHandler>();

        // Scanning
        services.AddTransient<IJobHandler, ScanLibraryJobHandler>();
        services.AddTransient<IJobHandler, ScanGalleryJobHandler>();
        services.AddTransient<IJobHandler, ScanBookJobHandler>();
        services.AddTransient<IJobHandler, ScanAudioJobHandler>();

        // Probing
        services.AddTransient<IJobHandler, ProbeVideoJobHandler>();
        services.AddTransient<IJobHandler, ProbeAudioJobHandler>();

        // Fingerprinting (single handler, registered per job type)
        services.AddTransient<IJobHandler>(sp => new FingerprintJobHandler(
            JobType.FingerprintVideo,
            sp.GetRequiredService<ILogger<FingerprintJobHandler>>(),
            sp.GetRequiredService<IMediaHashing>(),
            sp.GetRequiredService<IMediaProcessingStatePersistence>()));
        services.AddTransient<IJobHandler>(sp => new FingerprintJobHandler(
            JobType.FingerprintImage,
            sp.GetRequiredService<ILogger<FingerprintJobHandler>>(),
            sp.GetRequiredService<IMediaHashing>(),
            sp.GetRequiredService<IMediaProcessingStatePersistence>()));
        services.AddTransient<IJobHandler>(sp => new FingerprintJobHandler(
            JobType.FingerprintAudio,
            sp.GetRequiredService<ILogger<FingerprintJobHandler>>(),
            sp.GetRequiredService<IMediaHashing>(),
            sp.GetRequiredService<IMediaProcessingStatePersistence>()));

        // Preview / asset generation
        services.AddTransient<IJobHandler, GeneratePreviewJobHandler>();
        services.AddTransient<IJobHandler, GenerateImageThumbnailJobHandler>();
        services.AddTransient<IJobHandler, GenerateBookPageThumbnailJobHandler>();
        services.AddTransient<IJobHandler, GenerateAudioWaveformJobHandler>();
        services.AddTransient<IJobHandler, ExtractSubtitlesJobHandler>();

        // Metadata / collections / maintenance
        services.AddTransient<IJobHandler, ImportMetadataJobHandler>();
        services.AddTransient<IJobHandler, RefreshCollectionJobHandler>();
        services.AddTransient<IJobHandler, RefreshEntityJobHandler>();
        services.AddTransient<IJobHandler, LibraryMaintenanceJobHandler>();

        // Identify
        services.AddTransient<IJobHandler, BulkIdentifyJobHandler>();

        // Background services
        services.AddSingleton<WorkerRuntimeIdentity>();
        services.AddHostedService<WorkerHeartbeatPublisher>();
        services.AddHostedService<QueueWorker>();
        services.AddHostedService<JobScheduler>();
        services.AddHostedService<JobHistoryPruner>();

        return services;
    }
}
