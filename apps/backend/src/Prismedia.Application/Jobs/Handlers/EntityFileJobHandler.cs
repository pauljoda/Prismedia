using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Base class for job handlers that operate on a single entity identified by
/// <see cref="JobRunSnapshot.TargetEntityId"/> and backed by a source file on disk.
/// Subclasses implement <see cref="ExecuteAsync"/> with the validated entity ID and file path.
/// </summary>
public abstract class EntityFileJobHandler(
    ILogger logger,
    IMediaProcessingStatePersistence persistence) : IJobHandler {
    public abstract JobType Type { get; }

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var entityId = ParseEntityId(context.Job.TargetEntityId);
        if (entityId is null) return;

        var filePath = await persistence.GetSourceFilePathAsync(entityId.Value, cancellationToken);
        if (filePath is null || !ValidateFilePath(filePath)) {
            logger.LogWarning("{JobType}: source file not found for {EntityId}", Type.ToCode(), entityId);
            await OnSourceFileNotFoundAsync(entityId.Value, cancellationToken);
            return;
        }

        await ExecuteAsync(context, entityId.Value, filePath, cancellationToken);
    }

    /// <summary>Performs the handler's work after entity ID resolution and source file validation.</summary>
    protected abstract Task ExecuteAsync(
        JobContext context, Guid entityId, string filePath, CancellationToken cancellationToken);

    /// <summary>Validates that the source file exists. Override for non-standard path formats.</summary>
    protected virtual bool ValidateFilePath(string filePath) => File.Exists(filePath);

    /// <summary>Called when the source file is not found, before returning. Override to perform cleanup.</summary>
    protected virtual Task OnSourceFileNotFoundAsync(Guid entityId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Scan persistence port for subclass use.</summary>
    protected IMediaProcessingStatePersistence Persistence => persistence;

    private static Guid? ParseEntityId(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}
