using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Jobs;
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
        ILibraryScanRootPersistence scanRoots) {
        _queue = queue;
        _maintenance = maintenance;
        _downstreamNeeds = downstreamNeeds;
        _scanRoots = scanRoots;
    }

    /// <summary>
    /// Lists active and recent job runs for the operations dashboard.
    /// </summary>
    public async Task<JobListResponse> ListAsync(bool hideNsfw, CancellationToken cancellationToken) {
        var items = (await _queue.ListAsync(hideNsfw, cancellationToken)).Select(ToContract).ToArray();
        var counts = (await _queue.GetQueueCountsAsync(hideNsfw, cancellationToken))
            .Select(c => new JobQueueCountDto(c.TypeCode, c.StatusCode, c.Count))
            .ToArray();
        return new JobListResponse(items, counts);
    }

    /// <summary>
    /// Creates a job from a typed queue operation.
    /// </summary>
    public async Task<JobCreateResponse> CreateAsync(JobType type, CancellationToken cancellationToken) {
        var job = await _queue.EnqueueAsync(type, cancellationToken);
        return new JobCreateResponse(ToContract(job));
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
        foreach (var (kind, jobType) in previewKinds) {
            var entityIds = await _maintenance.GetActiveEntityIdsByKindAsync(kind, cancellationToken);
            foreach (var entityId in entityIds) {
                var id = entityId.ToString();
                await _maintenance.ClearGeneratedPreviewAssetsAsync(kind, entityId, cancellationToken);

                if (await _queue.HasPendingAsync(jobType, id, cancellationToken)) {
                    skipped++;
                    continue;
                }

                await _queue.EnqueueAsync(
                    new EnqueueJobRequest(
                        Type: jobType,
                        TargetEntityKind: EntityKindRegistry.ToCode(kind),
                        TargetEntityId: id),
                    cancellationToken);
                enqueued++;
            }
        }

        return new BulkJobResponse(enqueued, skipped);
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
            return new BulkJobResponse(0, 0);
        }

        var fingerprintKinds = new (EntityKind Kind, JobType JobType)[]
        {
            (EntityKind.Video, JobType.FingerprintVideo),
            (EntityKind.Image, JobType.FingerprintImage),
            (EntityKind.AudioTrack, JobType.FingerprintAudio),
        };

        int enqueued = 0, skipped = 0;
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

                await _queue.EnqueueAsync(
                    new EnqueueJobRequest(
                        Type: jobType,
                        TargetEntityKind: EntityKindRegistry.ToCode(kind),
                        TargetEntityId: id),
                    cancellationToken);
                enqueued++;
            }
        }

        return new BulkJobResponse(enqueued, skipped);
    }

    private static JobRun ToContract(JobRunSnapshot job) =>
        new(
            job.Id,
            job.Type.ToCode(),
            job.Status.ToCode(),
            job.Progress,
            job.Message,
            job.TargetEntityKind,
            job.TargetEntityId,
            job.TargetLabel,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt);
}
