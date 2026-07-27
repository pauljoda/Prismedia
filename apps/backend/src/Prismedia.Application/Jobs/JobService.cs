using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Jobs;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Application use-case service for listing, creating, and bulk-orchestrating background jobs.
/// </summary>
public sealed class JobService {
    private readonly IJobQueueService _queue;
    private readonly IMaintenancePersistence _maintenance;
    private readonly IDownstreamNeedsPersistence _downstreamNeeds;
    private readonly ILibraryScanRootPersistence _scanRoots;
    private readonly IJobGraphService _graphs;
    private readonly IAcquisitionGraphCancellation? _acquisitions;

    /// <summary>
    /// Creates a job service over the durable queue and maintenance persistence ports.
    /// </summary>
    /// <param name="queue">Queue port implemented by infrastructure persistence.</param>
    /// <param name="maintenance">Maintenance persistence port used to enumerate active entities for bulk operations.</param>
    /// <param name="downstreamNeeds">Persistence port used to check existing fingerprints during bulk backfill.</param>
    /// <param name="scanRoots">Persistence port used to read generation settings during bulk backfill.</param>
    public JobService(
        IJobQueueService queue,
        IMaintenancePersistence maintenance,
        IDownstreamNeedsPersistence downstreamNeeds,
        ILibraryScanRootPersistence scanRoots,
        IJobGraphService graphs,
        IAcquisitionGraphCancellation? acquisitions = null) {
        _queue = queue;
        _maintenance = maintenance;
        _downstreamNeeds = downstreamNeeds;
        _scanRoots = scanRoots;
        _graphs = graphs;
        _acquisitions = acquisitions;
    }

    /// <summary>
    /// Lists active and recent job runs for the operations dashboard.
    /// </summary>
    public async Task<JobListResponse> ListAsync(bool hideNsfw, CancellationToken cancellationToken) {
        var items = (await _queue.ListAsync(hideNsfw, cancellationToken)).Select(ToContract).ToArray();
        var counts = (await _queue.GetQueueCountsAsync(hideNsfw, cancellationToken))
            .Select(c => new JobQueueCountDto(c.TypeCode.DecodeAs<JobType>(), c.StatusCode.DecodeAs<JobRunStatus>(), c.Count))
            .ToArray();
        return new JobListResponse(items, counts);
    }

    /// <summary>
    /// Creates a job from a typed queue operation.
    /// </summary>
    public async Task<JobCreateResponse> CreateAsync(JobType type, CancellationToken cancellationToken) {
        var job = await _queue.EnqueueAsync(type, cancellationToken);
        var contract = ToContract(job);
        return new JobCreateResponse(contract, ToGraphReference(job, contract));
    }

    /// <summary>Lists durable logical lanes with aggregate node progress and blocking reason.</summary>
    public Task<JobGraphListResponse> ListGraphsAsync(CancellationToken cancellationToken) =>
        ListGraphsAsync(hideNsfw: false, cancellationToken);

    /// <summary>Lists durable logical lanes with aggregate node progress and blocking reason.</summary>
    public async Task<JobGraphListResponse> ListGraphsAsync(bool hideNsfw, CancellationToken cancellationToken) {
        var graphs = await _graphs.ListAsync(hideNsfw, cancellationToken);
        var results = new List<JobGraphSummary>(graphs.Count);
        foreach (var graph in graphs) {
            var detail = await _graphs.GetAsync(graph.Id, hideNsfw, cancellationToken);
            if (detail is not null) results.Add(ToSummary(detail));
        }
        return new JobGraphListResponse(results);
    }

    /// <summary>Returns one expanded graph, or null when it does not exist.</summary>
    public Task<JobGraphDetailResponse?> GetGraphAsync(Guid graphId, CancellationToken cancellationToken) =>
        GetGraphAsync(graphId, hideNsfw: false, cancellationToken);

    /// <summary>Returns one expanded graph, or null when it does not exist.</summary>
    public async Task<JobGraphDetailResponse?> GetGraphAsync(
        Guid graphId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var detail = await _graphs.GetAsync(graphId, hideNsfw, cancellationToken);
        if (detail is null) return null;
        return new JobGraphDetailResponse(
            ToSummary(detail),
            detail.Nodes.Select(node => new JobGraphNode(
                node.Id,
                node.NodeKey,
                node.ParentRunId,
                node.Type,
                node.Status,
                node.Importance,
                node.ResourceClass,
                node.ResourceKey,
                node.Progress,
                node.Message,
                node.TargetEntityKind,
                node.TargetEntityId,
                node.TargetLabel,
                node.CreatedAt,
                node.StartedAt,
                node.FinishedAt)).ToArray(),
            detail.Dependencies.Select(edge => new JobGraphDependency(
                edge.PredecessorRunId,
                edge.SuccessorRunId)).ToArray(),
            detail.Signals.Select(signal => new JobGraphSignal(
                signal.Id,
                signal.Key,
                signal.Kind,
                signal.CorrelationId,
                signal.Message,
                signal.CreatedAt,
                signal.ResolvedAt,
                signal.CancelledAt)).ToArray());
    }

    /// <summary>Cancels one graph, all active nodes, and all open signals.</summary>
    public async Task<JobGraphCancelResponse> CancelGraphAsync(Guid graphId, CancellationToken cancellationToken) {
        var detail = await _graphs.GetAsync(graphId, cancellationToken);
        var cancelled = await _graphs.CancelAsync(graphId, cancellationToken);
        if (cancelled && _acquisitions is not null && detail is not null) {
            var acquisitionIds = detail.Signals
                .Where(signal => signal.Kind == JobGraphSignalKind.ExternalTransfer)
                .Select(signal => Guid.TryParse(signal.CorrelationId, out var id) ? id : (Guid?)null)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct();
            foreach (var acquisitionId in acquisitionIds) {
                await _acquisitions.CancelFromGraphAsync(acquisitionId, cancellationToken);
            }
        }
        return new JobGraphCancelResponse(cancelled);
    }

    /// <summary>
    /// Cancels queued or running jobs, optionally scoped to one typed operation.
    /// </summary>
    public async Task<JobCancelResponse> CancelAsync(JobType? type, CancellationToken cancellationToken) {
        var cancelled = await _queue.CancelAsync(type, cancellationToken);
        return new JobCancelResponse(cancelled);
    }

    /// <summary>
    /// Cancels a single queued or running job by identifier.
    /// </summary>
    public async Task<JobCancelResponse> CancelRunAsync(Guid id, CancellationToken cancellationToken) {
        var cancelled = await _queue.CancelRunAsync(id, cancellationToken);
        return new JobCancelResponse(cancelled ? 1 : 0);
    }

    /// <summary>
    /// Clears failed jobs from the active failure list, optionally scoped to one typed operation.
    /// </summary>
    public async Task<JobFailureClearResponse> ClearFailuresAsync(
        JobType? type,
        CancellationToken cancellationToken) {
        var cleared = await _queue.ClearFailuresAsync(type, cancellationToken);
        return new JobFailureClearResponse(cleared);
    }

    /// <summary>
    /// Enqueues preview-asset generation jobs for every active media entity that does not
    /// already have a matching job pending. Used by the operations dashboard "rebuild previews"
    /// maintenance action.
    /// </summary>
    public async Task<BulkJobResponse> RebuildPreviewsAsync(CancellationToken cancellationToken) {
        var previewKinds = new (EntityKind Kind, JobType JobType)[]
        {
            (EntityKind.Video, JobType.GeneratePreview),
            (EntityKind.Image, JobType.GenerateImageThumbnail),
            (EntityKind.BookPage, JobType.GenerateBookPageThumbnail),
            (EntityKind.AudioTrack, JobType.GenerateAudioWaveform),
        };

        int enqueued = 0, skipped = 0;
        var graphs = new List<JobGraphReference>();
        foreach (var (kind, jobType) in previewKinds) {
            var entityIds = await _maintenance.GetActiveEntityIdsByKindAsync(kind, cancellationToken);
            foreach (var entityId in entityIds) {
                var id = entityId.ToString();
                await _maintenance.ClearGeneratedPreviewAssetsAsync(kind, entityId, cancellationToken);

                if (await _queue.HasPendingAsync(jobType, id, cancellationToken)) {
                    skipped++;
                    continue;
                }

                var job = await _queue.EnqueueAsync(
                    new EnqueueJobRequest(
                        Type: jobType,
                        TargetEntityKind: EntityKindRegistry.ToCode(kind),
                        TargetEntityId: id),
                    cancellationToken);
                var contract = ToContract(job);
                graphs.Add(ToGraphReference(job, contract));
                enqueued++;
            }
        }

        return new BulkJobResponse(enqueued, skipped, graphs);
    }

    /// <summary>
    /// Enqueues fingerprint generation jobs for every active media entity that is missing an enabled
    /// fingerprint (oshash and/or MD5, per generation settings) and does not already have a fingerprint
    /// job pending. Used by the operations dashboard "backfill fingerprints" maintenance action. When
    /// both fingerprint algorithms are disabled, this is a no-op.
    /// </summary>
    public async Task<BulkJobResponse> BackfillFingerprintsAsync(CancellationToken cancellationToken) {
        var settings = await _scanRoots.GetSettingsAsync(cancellationToken);
        if (!settings.AutoGenerateOshash && !settings.AutoGenerateMd5) {
            return new BulkJobResponse(0, 0, []);
        }

        var fingerprintKinds = new (EntityKind Kind, JobType JobType)[]
        {
            (EntityKind.Video, JobType.FingerprintVideo),
            (EntityKind.Image, JobType.FingerprintImage),
            (EntityKind.AudioTrack, JobType.FingerprintAudio),
        };

        int enqueued = 0, skipped = 0;
        var graphs = new List<JobGraphReference>();
        foreach (var (kind, jobType) in fingerprintKinds) {
            var entityIds = await _maintenance.GetActiveEntityIdsByKindAsync(kind, cancellationToken);
            foreach (var entityId in entityIds) {
                if (!await Handlers.FingerprintGating.ShouldFingerprintAsync(
                        _downstreamNeeds, settings, entityId, cancellationToken)) {
                    skipped++;
                    continue;
                }

                var id = entityId.ToString();
                if (await _queue.HasPendingAsync(jobType, id, cancellationToken)) {
                    skipped++;
                    continue;
                }

                var job = await _queue.EnqueueAsync(
                    new EnqueueJobRequest(
                        Type: jobType,
                        TargetEntityKind: EntityKindRegistry.ToCode(kind),
                        TargetEntityId: id),
                    cancellationToken);
                var contract = ToContract(job);
                graphs.Add(ToGraphReference(job, contract));
                enqueued++;
            }
        }

        return new BulkJobResponse(enqueued, skipped, graphs);
    }

    private static JobRun ToContract(JobRunSnapshot job) =>
        new(
            job.Id,
            job.Type,
            job.Status,
            job.Progress,
            job.Message,
            job.TargetEntityKind,
            job.TargetEntityId,
            job.TargetLabel,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt);

    private static JobGraphReference ToGraphReference(JobRunSnapshot job, JobRun contract) =>
        new(
            job.GraphId ?? throw new InvalidOperationException("New job nodes must belong to a durable graph."),
            job.GraphOrigin ?? JobGraphOrigin.Background,
            job.TargetEntityKind,
            job.TargetEntityId,
            contract);

    private static JobGraphSummary ToSummary(JobGraphDetailSnapshot detail) {
        var nodes = detail.Nodes;
        var completed = nodes.Count(node => node.Status == JobRunStatus.Completed);
        var failed = nodes.Count(node => node.Status == JobRunStatus.Failed);
        var terminal = nodes.Count(node => node.Status is
            JobRunStatus.Completed or JobRunStatus.Failed or JobRunStatus.Cancelled);
        var warnings = nodes.Count(node =>
            node.Status == JobRunStatus.Failed && node.Importance == JobNodeImportance.BestEffort);
        var nodeStatusById = nodes.ToDictionary(node => node.Id, node => node.Status);
        var dependencyBlocked = detail.Dependencies
            .Where(edge => !nodeStatusById.TryGetValue(edge.PredecessorRunId, out var predecessorStatus)
                || predecessorStatus != JobRunStatus.Completed)
            .Select(edge => edge.SuccessorRunId)
            .ToHashSet();
        var current = nodes.FirstOrDefault(node => node.Status == JobRunStatus.Running)
            ?? nodes.FirstOrDefault(node =>
                node.Status == JobRunStatus.Queued && !dependencyBlocked.Contains(node.Id))
            ?? nodes.FirstOrDefault(node => node.Status == JobRunStatus.Queued);
        var openSignal = detail.Signals.FirstOrDefault(signal => signal.ResolvedAt is null && signal.CancelledAt is null);
        var waitReason = openSignal?.Message
            ?? (current?.Status == JobRunStatus.Queued
                ? dependencyBlocked.Contains(current.Id)
                    ? "Waiting for dependencies."
                    : current.ResourceKey is { } resourceKey
                        ? ResourceWaitReason(resourceKey)
                        : current.Message
                : null);
        var progress = nodes.Count == 0
            ? 0
            : (int)Math.Round(nodes.Average(node => node.Status is
                JobRunStatus.Completed or JobRunStatus.Failed or JobRunStatus.Cancelled
                    ? 100
                    : node.Progress));
        return new JobGraphSummary(
            detail.Graph.Id,
            detail.Graph.Origin,
            detail.Graph.Status,
            detail.Graph.DisplayName,
            detail.Graph.RootEntityKind,
            detail.Graph.RootEntityId,
            progress,
            nodes.Count,
            completed,
            failed,
            terminal,
            warnings,
            current?.Type,
            waitReason,
            detail.Graph.CreatedAt,
            detail.Graph.UpdatedAt,
            detail.Graph.FinishedAt);
    }

    private static string ResourceWaitReason(string resourceKey) {
        if (resourceKey.StartsWith(JobResourceKeys.PluginPrefix, StringComparison.Ordinal)) {
            var provider = resourceKey[JobResourceKeys.PluginPrefix.Length..];
            return $"Queued for {ProviderDisplayName(provider)}.";
        }

        if (resourceKey == JobResourceKeys.AcquisitionIndexerSearch) {
            return "Queued for an acquisition provider.";
        }

        return resourceKey.StartsWith(JobResourceKeys.EntityPrefix, StringComparison.Ordinal)
            ? "Queued for exclusive entity access."
            : "Waiting for a shared resource.";
    }

    private static string ProviderDisplayName(string provider) => provider.ToLowerInvariant() switch {
        WellKnownPluginIds.MusicBrainz => "MusicBrainz",
        WellKnownPluginIds.MangaDex => "MangaDex",
        WellKnownPluginIds.OpenLibrary => "Open Library",
        _ => provider
    };
}
