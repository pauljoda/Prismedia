using Microsoft.Extensions.Logging;
using Prismedia.Application.Books;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Books;

/// <summary>
/// Brings one Book's persisted readable chapters and automatic audiobook chapter map current.
/// Scans enqueue this only when the map service reports stale signatures, and the service itself
/// no-ops when inputs turn out unchanged, so re-runs are cheap and idempotent.
/// </summary>
[JobDefinition(JobType.MapBookChapters, ResourceClass = JobResourceClass.StandardCpu, Importance = JobNodeImportance.BestEffort)]
public sealed class MapBookChaptersJobHandler(
    ILogger<MapBookChaptersJobHandler> logger,
    IBookChapterMapService chapterMap) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var bookId)) {
            return;
        }

        var result = await chapterMap.RefreshAsync(bookId, cancellationToken);
        logger.LogInformation(
            "MapBookChapters: {Label} — contents {Contents}, auto map {Map}",
            context.Job.TargetLabel,
            result.ContentsRefreshed ? "refreshed" : "unchanged",
            result.AutoMappingsReplaced ? "recomputed" : "unchanged");
    }
}
