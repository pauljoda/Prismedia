using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Turns the typed choices in an <see cref="EntityProcessingPlan"/> into entity job requests.
/// Callers retain responsibility for graph dependencies and for deferring preview work that needs
/// a successful probe to populate technical metadata first.
/// </summary>
internal static class EntityProcessingPlanRequests {
    /// <summary>
    /// Builds requests in readiness-first order for one entity.
    /// </summary>
    /// <param name="kind">The concrete entity kind receiving the jobs.</param>
    /// <param name="entityId">The target entity identifier.</param>
    /// <param name="label">The human-readable target label.</param>
    /// <param name="settings">Current library processing settings.</param>
    /// <param name="needs">Persistence-derived work still needed by the entity.</param>
    /// <param name="deferPreviewUntilProbeCompletes">
    /// Suppresses preview work when this plan also requires a probe. A probe handler or durable
    /// graph must enqueue that preview behind the successful probe.
    /// </param>
    public static IReadOnlyList<EnqueueJobRequest> ForEntity(
        EntityKind kind,
        Guid entityId,
        string label,
        LibrarySettingsData settings,
        DownstreamNeeds needs,
        bool deferPreviewUntilProbeCompletes = false) {
        var plan = EntityKindRegistry.Describe(kind).Processing.Plan(
            EntityProcessingInputAdapter.From(
                settings,
                needs,
                forceSubtitleReconciliationForOwnedSource: false));
        var requests = new List<EnqueueJobRequest>(6);
        var entityIdText = entityId.ToString();

        Add(plan.ProbeJobType);
        Add(plan.FingerprintJobType);
        Add(plan.SubtitleExtractionJobType);
        if (!deferPreviewUntilProbeCompletes || plan.ProbeJobType is null) {
            Add(plan.PreviewJobType);
            Add(plan.TrickplayJobType);
        }

        if (plan.PreviewJobType is null) {
            Add(plan.GridThumbnailJobType);
        }

        return requests;

        void Add(JobType? type) {
            if (type is { } jobType) {
                requests.Add(EnqueueJobRequest.ForEntity(jobType, kind, entityIdText, label));
            }
        }
    }
}
