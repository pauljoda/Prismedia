using Microsoft.Extensions.Logging;
using Prismedia.Application.Subtitles;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Identity;

/// <summary>Acquires missing configured languages using strict provider identity policy.</summary>
public sealed class AutoAcquireSubtitlesJobHandler(
    ISubtitleAcquisitionService subtitles,
    ILogger<AutoAcquireSubtitlesJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquireSubtitles;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var videoId)) {
            logger.LogWarning(
                "AcquireSubtitles: missing or invalid target video id '{TargetEntityId}'",
                context.Job.TargetEntityId);
            return;
        }

        await context.ReportProgressAsync(10, "Checking preferred subtitle languages", cancellationToken);
        AutomaticSubtitleAcquisitionResult result;
        try {
            result = await subtitles.AcquireMissingPreferredAsync(videoId, cancellationToken);
        } catch (SubtitleProviderUnavailableException exception) {
            throw new JobRetryLaterException(exception.Message, TimeSpan.FromMinutes(15));
        }

        var message = result.SkipReason ?? (result.DownloadedCount > 0
            ? $"Acquired {result.DownloadedCount} subtitle tracks"
            : result.MissingLanguages.Count > 0
                ? $"No automatic match for {string.Join(", ", result.MissingLanguages)}"
                : "Preferred subtitle languages are already available");
        await context.ReportProgressAsync(100, message, cancellationToken);
    }
}
