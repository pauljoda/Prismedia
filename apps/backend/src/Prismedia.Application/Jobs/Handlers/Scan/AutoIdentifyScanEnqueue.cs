using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Shared helper that queues auto-identify jobs for scanned media. Auto identify targets only
/// top-level ancestors: a series, album, or top gallery — never its children — so identifying a
/// parent cascades to its descendants instead of each child racing to re-identify the whole tree.
/// </summary>
internal static class AutoIdentifyScanEnqueue {
    private const int AutoIdentifyPriority = JobPriorities.AutoIdentify;

    /// <summary>
    /// Resolves the distinct top-level ancestors of the scanned entities and queues one auto-identify
    /// job per root whose media kind the user enabled. No-ops when auto identify is disabled.
    /// </summary>
    /// <param name="context">Job context used to enqueue work (deduplicates by target).</param>
    /// <param name="settings">Scan settings snapshot carrying the auto-identify gate.</param>
    /// <param name="downstreamNeeds">Persistence used to resolve top-level ancestors.</param>
    /// <param name="entityIds">Entities discovered during the scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnqueueRootsAsync(
        JobContext context,
        LibrarySettingsData settings,
        IDownstreamNeedsPersistence downstreamNeeds,
        IReadOnlyList<Guid> entityIds,
        CancellationToken cancellationToken) {
        if (!settings.AutoIdentifyEnabled || settings.AutoIdentifyKinds is not { Count: > 0 } || entityIds.Count == 0) {
            return;
        }

        var roots = await downstreamNeeds.ResolveAutoIdentifyRootsAsync(entityIds, cancellationToken);
        foreach (var root in roots) {
            var request = RequestFor(settings, root.KindCode, root.Id.ToString(), root.Title);
            if (request is not null) {
                await context.EnqueueIfNeededAsync(request, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds an auto-identify enqueue request for one top-level entity, or null when auto identify is
    /// off, the entity kind is not auto-identifiable, or its selector kind is not selected.
    /// </summary>
    /// <param name="settings">Scan settings snapshot carrying the auto-identify gate.</param>
    /// <param name="entityKind">Stable entity kind code; mapped to a media selector kind.</param>
    /// <param name="entityId">Entity to identify.</param>
    /// <param name="label">Human-readable label for job dashboards.</param>
    public static EnqueueJobRequest? RequestFor(
        LibrarySettingsData settings,
        string entityKind,
        string entityId,
        string label) {
        if (!settings.AutoIdentifyEnabled || settings.AutoIdentifyKinds is not { Count: > 0 } kinds) {
            return null;
        }

        if (!AutoIdentifySelectorKinds.TryMap(entityKind, out var selectorKind) ||
            !kinds.Contains(selectorKind, StringComparer.OrdinalIgnoreCase)) {
            return null;
        }

        return new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: entityKind,
            TargetEntityId: entityId,
            TargetLabel: label,
            Priority: AutoIdentifyPriority);
    }

    /// <summary>
    /// Builds an auto-identify enqueue request for one typed entity kind.
    /// </summary>
    public static EnqueueJobRequest? RequestFor(
        LibrarySettingsData settings,
        EntityKind entityKind,
        string entityId,
        string label) =>
        RequestFor(settings, entityKind.ToCode(), entityId, label);
}
