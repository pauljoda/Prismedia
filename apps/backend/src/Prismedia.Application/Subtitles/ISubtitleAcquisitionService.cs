using Prismedia.Domain.Entities;

namespace Prismedia.Application.Subtitles;

/// <summary>
/// Application boundary for subtitle provider configuration, ranked discovery, and acquisition.
/// Implementations own external-provider and persistence details.
/// </summary>
public interface ISubtitleAcquisitionService {
    /// <summary>
    /// Resolves the concrete directly playable Entity kind addressed by a video-route request.
    /// Implementations must reject missing or non-playable entities rather than guessing Video.
    /// </summary>
    Task<EntityKind> ResolvePlayableVideoKindAsync(Guid videoId, CancellationToken cancellationToken);

    Task<OpenSubtitlesConfiguration> GetOpenSubtitlesConfigurationAsync(CancellationToken cancellationToken);

    Task<OpenSubtitlesConfiguration> SaveOpenSubtitlesConfigurationAsync(
        SaveOpenSubtitlesConfiguration configuration,
        CancellationToken cancellationToken);

    Task<SubtitleProviderTestResult> TestOpenSubtitlesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        Guid videoId,
        SubtitleSearchRequest request,
        CancellationToken cancellationToken);

    Task<SubtitleAcquisitionResult> AcquireAsync(
        Guid videoId,
        string provider,
        string candidateId,
        CancellationToken cancellationToken);

    Task<AutomaticSubtitleAcquisitionResult> AcquireMissingPreferredAsync(
        Guid videoId,
        CancellationToken cancellationToken);
}
