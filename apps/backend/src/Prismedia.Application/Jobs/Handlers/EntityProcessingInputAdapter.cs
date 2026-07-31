using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Converts current library settings and persistence-derived needs into the domain processing input.</summary>
public static class EntityProcessingInputAdapter {
    /// <summary>Creates the one immutable input consumed by <see cref="EntityProcessingPolicy.Plan"/>.</summary>
    public static EntityProcessingInputs From(
        LibrarySettingsData settings,
        DownstreamNeeds needs,
        bool forceSubtitleReconciliationForOwnedSource) =>
        new(
            needs.NeedsProbe,
            FingerprintGating.ShouldFingerprint(settings, needs),
            needs.NeedsSubtitleExtraction,
            forceSubtitleReconciliationForOwnedSource,
            needs.NeedsPreview,
            needs.NeedsTrickplay,
            needs.NeedsGridThumbnail,
            settings.AutoGenerateMetadata,
            settings.AutoGeneratePreview,
            settings.GenerateTrickplay);
}
