using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Re-evaluates dynamic collection rules and replaces membership with newly resolved entities.
/// Manual items are preserved; dynamic items are replaced atomically.
/// </summary>
public sealed class RefreshCollectionJobHandler(
    ILogger<RefreshCollectionJobHandler> logger,
    ICollectionRefreshPersistence persistence,
    ICollectionRuleEngine ruleEngine) : IJobHandler {
    public JobType Type => JobType.RefreshCollection;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var collectionId = ParseEntityId(context.Job.TargetEntityId);
        if (collectionId is null) {
            await RefreshAllAsync(context, cancellationToken);
            return;
        }

        var collection = await persistence.GetDynamicCollectionAsync(collectionId.Value, cancellationToken);
        if (collection is null) {
            logger.LogWarning("RefreshCollection: collection {Id} not found or not dynamic", collectionId);
            await context.ReportProgressAsync(100, "Collection not found or not dynamic", cancellationToken);
            return;
        }

        await RefreshOneAsync(collection, context, cancellationToken);
    }

    private async Task RefreshAllAsync(JobContext context, CancellationToken cancellationToken) {
        var collections = await persistence.ListDynamicCollectionsAsync(cancellationToken);
        if (collections.Count == 0) {
            await context.ReportProgressAsync(100, "No dynamic collections to refresh", cancellationToken);
            return;
        }

        var refreshed = 0;
        var resolved = 0;
        for (var index = 0; index < collections.Count; index++) {
            var collection = collections[index];
            var progress = Math.Clamp((int)Math.Floor(index / (double)collections.Count * 95), 0, 95);
            await context.ReportProgressAsync(progress, $"Refreshing {collection.Title}", cancellationToken);
            var count = await RefreshOneAsync(collection, context, cancellationToken, reportProgress: false);
            refreshed++;
            resolved += count;
        }

        logger.LogInformation(
            "RefreshCollection: refreshed {CollectionCount} dynamic collection(s) with {ItemCount} resolved item(s)",
            refreshed, resolved);

        await context.ReportProgressAsync(
            100,
            $"Refreshed {refreshed} collections with {resolved} items",
            cancellationToken);
    }

    private async Task<int> RefreshOneAsync(
        CollectionRefreshData collection,
        JobContext context,
        CancellationToken cancellationToken,
        bool reportProgress = true) {
        if (reportProgress) {
            await context.ReportProgressAsync(10, "Evaluating rules", cancellationToken);
        }

        var matches = await ruleEngine.EvaluateAsync(
            collection.RuleTreeJson,
            collection.OwnerUserId,
            cancellationToken);

        if (reportProgress) {
            await context.ReportProgressAsync(50, $"Resolved {matches.Count} entities, updating membership", cancellationToken);
        }

        await persistence.RefreshCollectionItemsAsync(collection.EntityId, matches, cancellationToken);

        logger.LogInformation(
            "RefreshCollection: updated {Title} with {Count} dynamic items",
            collection.Title, matches.Count);

        if (reportProgress) {
            await context.ReportProgressAsync(100, $"Refreshed with {matches.Count} items", cancellationToken);
        }

        return matches.Count;
    }

    private static Guid? ParseEntityId(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}
