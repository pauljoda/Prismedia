using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Base class for library scan handlers. Manages root ID parsing from the job payload,
/// root filtering by scan type, and the single-root vs. all-roots iteration pattern.
/// Subclasses implement <see cref="IsEligibleRoot"/> and <see cref="ScanRootAsync"/>.
/// </summary>
public abstract class ScanJobHandler(
    ILogger logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots) : IJobHandler {
    public abstract JobType Type { get; }

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!ScanRootPayload.TryParse(context.Job.PayloadJson, out var payload)) {
            var enabledRoots = await roots.GetEnabledRootsAsync(cancellationToken);
            var eligible = enabledRoots.Where(IsEligibleRoot).ToList();
            logger.LogInformation("{JobType}: scanning {Count} eligible roots", Type.ToCode(), eligible.Count);

            for (var i = 0; i < eligible.Count; i++) {
                await ScanRootAsync(context, eligible[i], cancellationToken);
                await roots.UpdateRootLastScannedAsync(eligible[i].Id, cancellationToken);
                await context.ReportProgressAsync((i + 1) * 100 / eligible.Count,
                    $"Scanned {eligible[i].Label}", cancellationToken);
            }
        } else {
            var root = await roots.GetLibraryRootAsync(payload.RootId, cancellationToken);
            if (root is null) {
                logger.LogWarning("{JobType}: root {RootId} not found", Type.ToCode(), payload.RootId);
                return;
            }

            await ScanRootAsync(context, root, cancellationToken);
            await roots.UpdateRootLastScannedAsync(root.Id, cancellationToken);
            await context.ReportProgressAsync(100, $"Scanned {root.Label}", cancellationToken);
        }
    }

    /// <summary>Returns true if this root should be scanned by this handler's media type.</summary>
    protected abstract bool IsEligibleRoot(LibraryRootData root);

    /// <summary>Discovers files, creates/updates entities, and enqueues downstream jobs for one root.</summary>
    protected abstract Task ScanRootAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken);

    /// <summary>File discovery port for subclass use.</summary>
    protected IFileDiscovery FileDiscovery => fileDiscovery;

    /// <summary>Root and scan-setting persistence port for subclass use.</summary>
    protected ILibraryScanRootPersistence Roots => roots;
}
