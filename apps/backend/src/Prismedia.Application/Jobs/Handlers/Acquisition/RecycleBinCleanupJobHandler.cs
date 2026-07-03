using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Daily recycle-bin purge: deletes binned files older than the configured cleanup window and drops
/// emptied dated subfolders. Scheduled only while a recycle-bin folder is configured.
/// </summary>
public sealed class RecycleBinCleanupJobHandler(IRecycleBin recycleBin, ILogger<RecycleBinCleanupJobHandler> logger) : IJobHandler {
    public JobType Type => JobType.RecycleBinCleanup;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var removed = await recycleBin.CleanupAsync(cancellationToken);
        if (removed > 0) {
            logger.LogInformation("RecycleBinCleanup: purged {Count} expired file(s).", removed);
        }

        await context.ReportProgressAsync(100, removed > 0 ? $"Purged {removed} expired file(s)" : "Nothing to purge", cancellationToken);
    }
}
