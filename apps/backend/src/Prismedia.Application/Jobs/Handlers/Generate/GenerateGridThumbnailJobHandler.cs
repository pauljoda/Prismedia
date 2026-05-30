using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Generate;

/// <summary>
/// Generates the small grid-card cover variant for an entity that already has a
/// cover. Enqueued by the library scan to backfill libraries whose covers predate
/// grid thumbnails; new entities get theirs as part of <see cref="GeneratePreviewJobHandler"/>.
/// </summary>
public sealed class GenerateGridThumbnailJobHandler(
    ILogger<GenerateGridThumbnailJobHandler> logger,
    IGridThumbnailService gridThumbnails) : IJobHandler {
    /// <inheritdoc />
    public JobType Type => JobType.GenerateGridThumbnail;

    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!Guid.TryParse(context.Job.TargetEntityId, out var entityId)) {
            logger.LogWarning("GenerateGridThumbnail: missing or invalid target entity id");
            return;
        }

        await gridThumbnails.EnsureAsync(entityId, cancellationToken);
        await context.ReportProgressAsync(100, "Grid thumbnail complete", cancellationToken);
    }
}
