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
        await context.ReportProgressAsync(10, $"Refreshing {needed.Count} thumbnail chains", cancellationToken);
        await gridThumbnails.EnsureManyAsync(needed, cancellationToken);
        await context.ReportProgressAsync(100, $"Refreshed {needed.Count} thumbnail chains", cancellationToken);
    }
}
