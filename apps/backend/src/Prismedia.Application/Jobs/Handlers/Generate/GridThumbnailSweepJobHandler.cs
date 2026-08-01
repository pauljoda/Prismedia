using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Library-wide self-heal for grid-card cover variants: finds every entity whose
/// variants are missing, older than the cover they derive from, or gone from disk,
/// and regenerates them. Scheduled at worker startup and daily thereafter so
/// libraries whose artwork predates grid thumbnails converge without user action.
/// </summary>
[JobDefinition(JobType.GridThumbnailSweep, SingletonBehavior = JobSingletonBehavior.QueueWide)]
public sealed class GridThumbnailSweepJobHandler(
    ILogger<GridThumbnailSweepJobHandler> logger,
    IGridThumbnailService gridThumbnails) : IJobHandler {
    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var needed = await gridThumbnails.ListEntitiesNeedingRefreshAsync(cancellationToken);
        if (needed.Count == 0) {
            await context.ReportProgressAsync(100, "Grid thumbnails up to date", cancellationToken);
            return;
        }

        logger.LogInformation("GridThumbnailSweep: refreshing grid thumbnails for {Count} entities", needed.Count);
        var done = 0;
        foreach (var entityId in needed) {
            cancellationToken.ThrowIfCancellationRequested();
            await gridThumbnails.EnsureAsync(entityId, cancellationToken);
            done++;
            if (done % 25 == 0 || done == needed.Count) {
                await context.ReportProgressAsync(
                    done * 100 / needed.Count,
                    $"Generated grid thumbnails for {done}/{needed.Count} entities",
                    cancellationToken);
            }
        }
    }
}
