using Microsoft.Extensions.Logging;
using Prismedia.Application.Backups;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Creates the retained daily database backup from the background queue.
/// </summary>
[JobDefinition(JobType.DatabaseBackup, SingletonBehavior = JobSingletonBehavior.QueueWide)]
public sealed class DatabaseBackupJobHandler(
    IDatabaseBackupService backups,
    ILogger<DatabaseBackupJobHandler> logger) : IJobHandler {
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(5, "Starting database backup", cancellationToken);
        var backup = await backups.CreateAutomaticBackupAsync(cancellationToken);
        await context.ReportProgressAsync(95, $"Created {backup.FileName}", cancellationToken);

        var pruned = await backups.PruneExpiredAutomaticBackupsAsync(cancellationToken);
        if (pruned > 0) {
            logger.LogInformation("Pruned {Count} expired automatic database backup(s).", pruned);
        }

        await context.ReportProgressAsync(100, "Database backup complete", cancellationToken);
    }
}
