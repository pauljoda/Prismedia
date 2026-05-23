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
            logger.LogWarning("RefreshCollection: no target entity ID provided");
            await context.ReportProgressAsync(100, "No collection ID", cancellationToken);
            return;
        }

        var collection = await persistence.GetDynamicCollectionAsync(collectionId.Value, cancellationToken);
        if (collection is null) {
            logger.LogWarning("RefreshCollection: collection {Id} not found or not dynamic", collectionId);
            await context.ReportProgressAsync(100, "Collection not found or not dynamic", cancellationToken);
            return;
        }

        await context.ReportProgressAsync(10, "Evaluating rules", cancellationToken);

        var matches = await ruleEngine.EvaluateAsync(collection.RuleTreeJson, cancellationToken);

        await context.ReportProgressAsync(50, $"Resolved {matches.Count} entities, updating membership", cancellationToken);

        await persistence.RefreshCollectionItemsAsync(collectionId.Value, matches, cancellationToken);

        logger.LogInformation(
            "RefreshCollection: updated {Title} with {Count} dynamic items",
            collection.Title, matches.Count);

        await context.ReportProgressAsync(100, $"Refreshed with {matches.Count} items", cancellationToken);
    }

    private static Guid? ParseEntityId(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}
