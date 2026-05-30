using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Scan;

/// <summary>
/// Shared gate that decides whether a scanned entity should be queued for auto identify.
/// Auto identify must be enabled and the entity's high-level selector kind must be one the user
/// chose. The un-organized-only filter is enforced later by the auto-identify runner so it sees the
/// entity's organized state at apply time, keeping scan handlers free of extra per-entity queries.
/// </summary>
internal static class AutoIdentifyScanEnqueue {
    private const int AutoIdentifyPriority = 15;

    /// <summary>
    /// Builds an auto-identify enqueue request for one scanned entity, or null when auto identify is
    /// off or the entity's kind is not selected.
    /// </summary>
    /// <param name="settings">Scan settings snapshot carrying the auto-identify gate.</param>
    /// <param name="selectorKind">High-level selector kind (video, gallery, image, audio, book).</param>
    /// <param name="entityKind">Stable entity kind code used as the job's target kind label.</param>
    /// <param name="entityId">Entity to identify.</param>
    /// <param name="label">Human-readable label for job dashboards.</param>
    public static EnqueueJobRequest? RequestFor(
        LibrarySettingsData settings,
        string selectorKind,
        string entityKind,
        string entityId,
        string label) {
        if (!settings.AutoIdentifyEnabled || settings.AutoIdentifyKinds is not { Count: > 0 } kinds) {
            return null;
        }

        if (!kinds.Contains(selectorKind, StringComparer.OrdinalIgnoreCase)) {
            return null;
        }

        return new EnqueueJobRequest(
            JobType.AutoIdentify,
            TargetEntityKind: entityKind,
            TargetEntityId: entityId,
            TargetLabel: label,
            Priority: AutoIdentifyPriority);
    }
}
