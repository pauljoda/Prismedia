using Prismedia.Application.Jobs;
using Prismedia.Application.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Subtitles;

/// <summary>Queues idempotent provider acquisition after local subtitle reconciliation.</summary>
public interface IAutomaticSubtitleAcquisitionScheduler {
    Task ScheduleAsync(
        JobContext context,
        Guid videoId,
        string label,
        CancellationToken cancellationToken);
}
/// <summary>Settings-aware durable queue planner for automatic subtitle acquisition.</summary>
public sealed class AutomaticSubtitleAcquisitionScheduler(
    SettingsService settings) : IAutomaticSubtitleAcquisitionScheduler {
    public async Task ScheduleAsync(
        JobContext context,
        Guid videoId,
        string label,
        CancellationToken cancellationToken) {
        var subtitleSettings = await settings.GetSubtitleSettingsAsync(cancellationToken);
        if (!subtitleSettings.AutoDownloadEnabled || subtitleSettings.AutoDownloadLanguages.Count == 0) {
            return;
        }

        if (!EntityKindRegistry.TryDescribe(context.Job.TargetEntityKind, out var definition)
            || definition is not IPlayableVideoKindDefinition) {
            throw new InvalidOperationException("Automatic subtitles require a playable video entity kind.");
        }

        await context.EnqueueIfNeededAsync(
            EnqueueJobRequest.ForEntity(
                JobType.AcquireSubtitles,
                definition.Kind,
                videoId.ToString(),
                label),
            cancellationToken);
    }
}
