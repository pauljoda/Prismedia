using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfHeldTvImportRecoveryStore {
    // The original review-flag migration required a claim id that older holds had already cleared.
    // A matching, completed automatic job can still prove authority. Never infer it from monitoring
    // alone: expired history, later decisions and explicit retries remain protected.
    private async Task RestoreRecordedAutomaticAuthorityAsync(CancellationToken cancellationToken) {
        var candidates = await LegacyReviewHolds().AsNoTracking().Select(row => new {
            row.Id, row.UpdatedAt, row.JobGraphId, row.SelectedReleaseJson,
            Latest = db.JobRuns.Where(job => job.Type == JobType.AcquisitionImport && job.TargetEntityId == row.Id.ToString())
                .OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id).FirstOrDefault()
        }).ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates) {
            var job = candidate.Latest;
            if (job is not { Status: JobRunStatus.Completed, FinishedAt: { } finishedAt }
                || finishedAt < candidate.UpdatedAt || job.GraphId != candidate.JobGraphId
                || !RecordedAutomaticClaim(job.PayloadJson, candidate.Id, candidate.SelectedReleaseJson)) continue;
            var unchanged = LegacyReviewHolds().Where(row => row.Id == candidate.Id
                && row.UpdatedAt == candidate.UpdatedAt && row.JobGraphId == candidate.JobGraphId
                && row.SelectedReleaseJson == candidate.SelectedReleaseJson
                && db.JobRuns.Where(run => run.Type == JobType.AcquisitionImport && run.TargetEntityId == row.Id.ToString())
                    .OrderByDescending(run => run.CreatedAt).ThenByDescending(run => run.Id)
                    .Select(run => run.Id == job.Id && run.Status == JobRunStatus.Completed && run.FinishedAt == finishedAt)
                    .FirstOrDefault());
            if (db.Database.IsRelational()) {
                if (await unchanged.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ImportManualReview, false), cancellationToken) == 1
                    && db.ChangeTracker.Entries<AcquisitionRow>().FirstOrDefault(entry => entry.Entity.Id == candidate.Id) is { } tracked)
                    await tracked.ReloadAsync(cancellationToken);
                continue;
            }
            if (await unchanged.SingleOrDefaultAsync(cancellationToken) is { } current) {
                current.ImportManualReview = false;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private IQueryable<AcquisitionRow> LegacyReviewHolds() => WithCompletedPayload().Where(row => row.ImportManualReview
        && row.Status == AcquisitionStatus.ManualImportRequired && row.ImportClaimJobId == null
        && row.JobGraphId != null && row.ImportCheckpointJson == null && row.UpgradeOfAcquisitionId == null
        && row.FinalSourcePath == null && row.ImportResultJson == null
        && (row.Kind == EntityKind.VideoSeason || row.Kind == EntityKind.VideoEpisode)
        && db.Monitors.Any(monitor => monitor.EntityId == row.EntityId && monitor.Status == MonitorStatus.Active));

    private static bool RecordedAutomaticClaim(string payloadJson, Guid acquisitionId, string? selectedJson) {
        try {
            var payload = AcquisitionJobPayload.Parse(payloadJson);
            if (payload.AcquisitionId != acquisitionId || payload.ManualRetry || payload.ManualReview
                || payload.AllowFormatChange || payload.ManualFileMappings is { Count: > 0 }
                || selectedJson is null || JsonSerializer.Deserialize<SelectedRelease>(selectedJson) is not { ManualPick: false }) return false;
            using var original = JsonDocument.Parse(payloadJson);
            using var canonical = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            return !CheckpointJsonCompatibility.HasUnknownMembers(original.RootElement, canonical.RootElement);
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException) {
            return false;
        }
    }
}
