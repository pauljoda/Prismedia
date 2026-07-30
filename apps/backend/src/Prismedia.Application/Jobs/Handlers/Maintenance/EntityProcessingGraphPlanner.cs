using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Derives the processing graph for one exact entity tree from its current capabilities, source state, and
/// server policy. The planner never enumerates a library root and is shared by imports and manual refreshes.
/// </summary>
public sealed class EntityProcessingGraphPlanner(
    ILogger<EntityProcessingGraphPlanner> logger,
    IEntityRefreshTreePersistence refreshTree,
    ILibraryScanRootPersistence scanRoots,
    IDownstreamNeedsPersistence downstreamNeeds,
    IMaintenancePersistence maintenance,
    ISubtitleSidecarDiscovery subtitleSidecars,
    IVideoScanPersistence videos) {

    /// <summary>
    /// Appends required readiness work first, an optional acquisition finalizer behind every required node,
    /// and best-effort asset/enrichment branches afterward.
    /// </summary>
    public async Task PlanAsync(
        JobContext context,
        AcquisitionFinalizeJobPayload? finalization,
        CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var entityId)) {
            logger.LogWarning("Entity processing: missing or invalid TargetEntityId");
            return;
        }

        var tree = await refreshTree.GetEntityTreeAsync(entityId, cancellationToken);
        if (tree.Count == 0) {
            throw new InvalidOperationException($"Entity '{entityId}' was not found for reconciliation.");
        }

        await context.ReportProgressAsync(10, $"Found {tree.Count} entities to reconcile", cancellationToken);
        await InvalidateChangedSubtitleSidecarsAsync(tree, cancellationToken);

        var settings = await scanRoots.GetSettingsAsync(cancellationToken);
        foreach (var entity in tree) {
            if (EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
                await maintenance.ClearGeneratedPreviewAssetsAsync(kind, entity.Id, cancellationToken);
            }
        }

        var needs = await downstreamNeeds.CheckDownstreamNeedsBatchAsync(
            tree.Select(entity => entity.Id).ToArray(),
            cancellationToken);
        var requiredReadiness = new List<Guid>();
        var bestEffort = new List<GraphJobNodeRequest>();

        foreach (var entity in tree) {
            if (!needs.TryGetValue(entity.Id, out var entityNeeds)
                || !EntityKindRegistry.TryGet(entity.KindCode, out var kind)) {
                continue;
            }

            var baseDependency = context.Job.Id;
            if (RequiredProbe(kind, settings, entityNeeds) is { } probeType) {
                var probe = await AppendAsync(
                    context,
                    Request(probeType, entity),
                    [context.Job.Id],
                    JobNodeImportance.Required,
                    cancellationToken);
                baseDependency = probe.Id;
                requiredReadiness.Add(probe.Id);
            }

            foreach (var request in BestEffortRequests(kind, entity, entityNeeds, settings)) {
                bestEffort.Add(Node(
                    request,
                    [baseDependency],
                    JobNodeImportance.BestEffort));
            }
        }

        Guid? finalizationNodeId = null;
        if (finalization is not null) {
            var dependencies = requiredReadiness.Count > 0
                ? requiredReadiness
                : [context.Job.Id];
            var finalizer = await context.AppendNodeAsync(
                Node(
                    new EnqueueJobRequest(
                        JobType.AcquisitionFinalize,
                        PayloadJson: finalization.ToJson(),
                        TargetEntityId: finalization.AcquisitionId.ToString(),
                        TargetLabel: "Finalize acquisition",
                        Importance: JobNodeImportance.Required,
                        ResourceClass: JobResourceClass.Light),
                    dependencies,
                    JobNodeImportance.Required),
                cancellationToken);
            finalizationNodeId = finalizer.Id;
        }

        foreach (var node in bestEffort) {
            await context.AppendNodeAsync(node, cancellationToken);
        }

        var touchedAncestors = (finalization?.TouchedAncestorIds ?? [])
            .Where(id => tree.All(entity => entity.Id != id))
            .Distinct()
            .ToArray();
        foreach (var ancestorId in touchedAncestors) {
            await context.AppendNodeAsync(
                Node(
                    new EnqueueJobRequest(
                        JobType.GenerateGridThumbnail,
                        TargetEntityKind: JobTargetKinds.Entity,
                        TargetEntityId: ancestorId.ToString(),
                        TargetLabel: "Refresh ancestor projection"),
                    [finalizationNodeId ?? context.Job.Id],
                    JobNodeImportance.BestEffort),
                cancellationToken);
        }

        if (context.Job.Type == JobType.ReconcileEntity
            && settings.AutoIdentifyEnabled
            && EntityKindRegistry.TryGet(tree[0].KindCode, out var rootKind)) {
            var payload = rootKind == EntityKind.Video
                ? new AutoIdentifyJobPayload(AllowChildTarget: true, IgnoreOrganizedGate: true).ToJson()
                : null;
            await context.AppendNodeAsync(
                Node(
                    EnqueueJobRequest.ForEntity(
                        JobType.AutoIdentify,
                        rootKind,
                        tree[0].Id.ToString(),
                        tree[0].Title,
                        payloadJson: payload),
                    [context.Job.Id],
                    JobNodeImportance.BestEffort),
                cancellationToken);
        }

        logger.LogInformation(
            "Entity processing: planned {Required} required and {BestEffort} best-effort nodes for {Label}",
            requiredReadiness.Count,
            bestEffort.Count,
            tree[0].Title);
        await context.ReportProgressAsync(
            100,
            $"Planned {requiredReadiness.Count + bestEffort.Count + touchedAncestors.Length + (finalization is null ? 0 : 1)} jobs",
            cancellationToken);
    }

    private static JobType? RequiredProbe(
        EntityKind kind,
        LibrarySettingsData settings,
        DownstreamNeeds entityNeeds) =>
        kind switch {
            EntityKind.Video when settings.AutoGenerateMetadata && entityNeeds.NeedsProbe => JobType.ProbeVideo,
            EntityKind.AudioTrack when entityNeeds.NeedsProbe => JobType.ProbeAudio,
            _ => null
        };

    private static IEnumerable<EnqueueJobRequest> BestEffortRequests(
        EntityKind kind,
        EntityRefreshTarget entity,
        DownstreamNeeds entityNeeds,
        LibrarySettingsData settings) {
        switch (kind) {
            case EntityKind.Video:
                if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                    yield return Request(JobType.FingerprintVideo, entity);
                if (entityNeeds.NeedsSubtitleExtraction || !string.IsNullOrWhiteSpace(entity.SourcePath))
                    yield return Request(JobType.ExtractSubtitles, entity);
                if ((settings.AutoGeneratePreview && entityNeeds.NeedsPreview)
                    || (settings.GenerateTrickplay && entityNeeds.NeedsTrickplay))
                    yield return Request(JobType.GeneratePreview, entity);
                break;
            case EntityKind.Image:
                if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                    yield return Request(JobType.FingerprintImage, entity);
                if (entityNeeds.NeedsPreview)
                    yield return Request(JobType.GenerateImageThumbnail, entity);
                break;
            case EntityKind.AudioTrack:
                if (FingerprintGating.ShouldFingerprint(settings, entityNeeds))
                    yield return Request(JobType.FingerprintAudio, entity);
                if (entityNeeds.NeedsPreview)
                    yield return Request(JobType.GenerateAudioWaveform, entity);
                break;
            case EntityKind.BookPage when entityNeeds.NeedsPreview:
                yield return Request(JobType.GenerateBookPageThumbnail, entity);
                break;
        }
    }

    private static EnqueueJobRequest Request(JobType type, EntityRefreshTarget entity) =>
        new(
            type,
            TargetEntityKind: entity.KindCode,
            TargetEntityId: entity.Id.ToString(),
            TargetLabel: entity.Title);

    private static GraphJobNodeRequest Node(
        EnqueueJobRequest request,
        IReadOnlyCollection<Guid> dependencies,
        JobNodeImportance importance) =>
        new(
            request.NodeKey ?? $"{request.Type.ToCode()}:{request.TargetEntityId}",
            request,
            DependsOn: dependencies,
            Importance: importance,
            ResourceClass: request.ResourceClass ?? JobDefinitionRegistry.ResourceClass(request.Type),
            ResourceKey: request.ResourceKey ?? (
                request.TargetEntityKind is not null && request.TargetEntityId is not null
                    ? JobResourceKeys.Entity(request.TargetEntityId)
                    : null));

    private static Task<JobRunSnapshot> AppendAsync(
        JobContext context,
        EnqueueJobRequest request,
        IReadOnlyCollection<Guid> dependencies,
        JobNodeImportance importance,
        CancellationToken cancellationToken) =>
        context.AppendNodeAsync(Node(request, dependencies, importance), cancellationToken);

    private async Task InvalidateChangedSubtitleSidecarsAsync(
        IReadOnlyList<EntityRefreshTarget> tree,
        CancellationToken cancellationToken) {
        var targets = tree
            .Where(entity =>
                string.Equals(entity.KindCode, EntityKind.Video.ToCode(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entity.SourcePath))
            .ToArray();
        if (targets.Length == 0) return;

        var sourcePaths = targets
            .Select(target => target.SourcePath!)
            .Distinct(FileSystemPathComparison.Comparer)
            .ToArray();
        var discoveries = await subtitleSidecars.DiscoverAsync(sourcePaths, cancellationToken);
        var discoveryByPath = discoveries
            .GroupBy(discovery => discovery.VideoPath, FileSystemPathComparison.Comparer)
            .ToDictionary(group => group.Key, group => group.Last(), FileSystemPathComparison.Comparer);
        var states = new List<VideoSubtitleSidecarState>(targets.Length);
        foreach (var target in targets) {
            if (!discoveryByPath.TryGetValue(target.SourcePath!, out var discovery) || !discovery.IsComplete) {
                throw new IOException("Adjacent subtitle discovery was incomplete; entity processing was not started.");
            }
            states.Add(new VideoSubtitleSidecarState(target.Id, discovery.Signature));
        }
        await videos.InvalidateSubtitleStateAsync(states, cancellationToken);
    }
}
