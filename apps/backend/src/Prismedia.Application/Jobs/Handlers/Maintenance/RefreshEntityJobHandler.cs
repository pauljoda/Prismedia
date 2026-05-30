using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Re-runs the processing pipeline (probe, fingerprint, preview, subtitles) for a single entity
/// and all of its structural children. Designed for "rescan this item" actions from detail pages.
/// </summary>
public sealed class RefreshEntityJobHandler(
    ILogger<RefreshEntityJobHandler> logger,
    IEntityRefreshTreePersistence refreshTree,
    ILibraryScanRootPersistence scanRoots,
    IDownstreamNeedsPersistence downstreamNeeds,
    IMaintenancePersistence maintenance) : IJobHandler {

    public JobType Type => JobType.RefreshEntity;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var entityId)) {
            logger.LogWarning("RefreshEntity: missing or invalid TargetEntityId");
            return;
        }

        var tree = await refreshTree.GetEntityTreeAsync(entityId, cancellationToken);
        if (tree.Count == 0) {
            logger.LogWarning("RefreshEntity: entity {EntityId} not found", entityId);
            return;
        }

        await context.ReportProgressAsync(10, $"Found {tree.Count} entities to refresh", cancellationToken);

        var settings = await scanRoots.GetSettingsAsync(cancellationToken);
        var ids = tree.Select(e => e.Id).ToList();
        foreach (var entity in tree) {
            if (EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
                await maintenance.ClearGeneratedPreviewAssetsAsync(kind, entity.Id, cancellationToken);
            }
        }

        var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync(ids, cancellationToken);

        var jobRequests = new List<EnqueueJobRequest>();
        foreach (var entity in tree) {
            if (!needs.TryGetValue(entity.Id, out var entityNeeds)) continue;
            var idStr = entity.Id.ToString();

            switch (entity.KindCode) {
                case "video":
                    if (settings.AutoGenerateMetadata && entityNeeds.NeedsProbe)
                        jobRequests.Add(new EnqueueJobRequest(JobType.ProbeVideo, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 30));
                    if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                        jobRequests.Add(new EnqueueJobRequest(JobType.FingerprintVideo, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 20));
                    if (entityNeeds.NeedsSubtitleExtraction)
                        jobRequests.Add(new EnqueueJobRequest(JobType.ExtractSubtitles, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 20));
                    if ((settings.AutoGeneratePreview && entityNeeds.NeedsPreview) || (settings.GenerateTrickplay && entityNeeds.NeedsTrickplay))
                        jobRequests.Add(new EnqueueJobRequest(JobType.GeneratePreview, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 10));
                    break;
                case "image":
                    if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                        jobRequests.Add(new EnqueueJobRequest(JobType.FingerprintImage, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 20));
                    if (entityNeeds.NeedsPreview)
                        jobRequests.Add(new EnqueueJobRequest(JobType.GenerateImageThumbnail, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 10));
                    break;
                case "audio-track":
                    if (entityNeeds.NeedsProbe)
                        jobRequests.Add(new EnqueueJobRequest(JobType.ProbeAudio, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 30));
                    if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                        jobRequests.Add(new EnqueueJobRequest(JobType.FingerprintAudio, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 20));
                    if (entityNeeds.NeedsPreview)
                        jobRequests.Add(new EnqueueJobRequest(JobType.GenerateAudioWaveform, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 10));
                    break;
                case "book-page":
                    if (entityNeeds.NeedsPreview)
                        jobRequests.Add(new EnqueueJobRequest(JobType.GenerateBookPageThumbnail, TargetEntityKind: entity.KindCode, TargetEntityId: idStr, TargetLabel: entity.Title, Priority: 10));
                    break;
            }
        }

        if (jobRequests.Count > 0) {
            var enqueued = await context.EnqueueBatchAsync(jobRequests, cancellationToken);
            logger.LogInformation("RefreshEntity: queued {Enqueued}/{Total} downstream jobs for {Label}",
                enqueued, jobRequests.Count, tree[0].Title);
        } else {
            logger.LogInformation("RefreshEntity: no downstream work needed for {Label}", tree[0].Title);
        }

        await context.ReportProgressAsync(100, $"Queued {jobRequests.Count} jobs", cancellationToken);
    }
}
