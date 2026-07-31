using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Performs library maintenance: validates generated cache assets exist on disk
/// and removes orphaned cache directories whose entities no longer exist.
/// </summary>
public sealed class LibraryMaintenanceJobHandler(
    ILogger<LibraryMaintenanceJobHandler> logger,
    IMaintenancePersistence persistence) : IJobHandler {
    public JobType Type => JobType.LibraryMaintenance;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        await context.ReportProgressAsync(5, "Starting maintenance", cancellationToken);

        var totalOrphansRemoved = 0;
        var totalMissingAssets = 0;
        var families = EntityKindRegistry.All
            .Select(definition => definition.Processing.AssetFamily)
            .Where(family => family != GeneratedAssetFamily.None)
            .Distinct()
            .OrderBy(family => family)
            .ToArray();
        var progressPerFamily = families.Length == 0 ? 90 : 90 / families.Length;

        for (var i = 0; i < families.Length; i++) {
            var family = families[i];
            var kinds = EntityKindRegistry.All
                .Where(definition => definition.Processing.AssetFamily == family)
                .Select(definition => definition.Kind)
                .ToArray();
            var entityIds = await persistence.GetActiveEntityIdsByKindsAsync(kinds, cancellationToken);

            var missing = await persistence.ValidateGeneratedAssetsAsync(family, entityIds, cancellationToken);
            totalMissingAssets += missing;
            var orphans = await persistence.CleanupOrphanedGeneratedAssetsAsync(family, entityIds, cancellationToken);
            totalOrphansRemoved += orphans;

            var progress = 5 + ((i + 1) * progressPerFamily);
            await context.ReportProgressAsync(progress,
                $"{family.ToCode()}: {entityIds.Count} entities, {missing} missing assets, {orphans} orphans cleaned",
                cancellationToken);
        }

        var orphanedSubtitleAssets = await persistence.CleanupOrphanedSubtitleAssetsAsync(cancellationToken);
        totalOrphansRemoved += orphanedSubtitleAssets;

        logger.LogInformation(
            "LibraryMaintenance complete: {MissingAssets} missing assets found, {OrphansRemoved} orphaned cache entries removed",
            totalMissingAssets, totalOrphansRemoved);

        await context.ReportProgressAsync(100,
            $"Maintenance complete: {totalMissingAssets} missing, {totalOrphansRemoved} orphans cleaned",
            cancellationToken);
    }

}
