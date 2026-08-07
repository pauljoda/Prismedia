namespace Prismedia.Application.Acquisition;

/// <summary>
/// Probes the owned and downloaded video files before an automatic atomic replacement so title-derived
/// quality can never hide a real resolution downgrade and a speculative subtitle upgrade must be proven.
/// </summary>
public interface IMediaUpgradePayloadInspector {
    /// <summary>Inspects the single video file in each payload path, or returns null when either payload is not safely probeable.</summary>
    Task<MediaUpgradePayloadInspection?> InspectAsync(
        string ownedContentPath,
        string candidateContentPath,
        CancellationToken cancellationToken);
}
