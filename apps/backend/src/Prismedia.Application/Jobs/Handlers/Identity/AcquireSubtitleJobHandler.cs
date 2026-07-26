using Microsoft.Extensions.Logging;
using Prismedia.Application.Subtitles;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Identity;

/// <summary>Downloads one user-selected subtitle candidate inside its interactive entity lane.</summary>
public sealed class AcquireSubtitleJobHandler(
    ISubtitleAcquisitionService subtitles,
    ILogger<AcquireSubtitleJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.AcquireSubtitle;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var payload = ManualSubtitleAcquisitionPayload.Parse(context.Job.PayloadJson);
        await context.ReportProgressAsync(10, "Downloading selected subtitle", cancellationToken);
        try {
            var result = await subtitles.AcquireAsync(
                payload.VideoId,
                payload.Provider,
                payload.CandidateId,
                cancellationToken);
            await context.ReportProgressAsync(
                100,
                result.AlreadyPresent ? "Subtitle already available" : "Subtitle imported",
                cancellationToken);
        } catch (SubtitleProviderUnavailableException exception) {
            logger.LogWarning(exception, "Subtitle provider temporarily unavailable for video {VideoId}", payload.VideoId);
            throw new JobRetryLaterException(exception.Message, TimeSpan.FromMinutes(5));
        }
    }
}
