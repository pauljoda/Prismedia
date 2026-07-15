namespace Prismedia.Application.Subtitles;

/// <summary>
/// Application boundary for subtitle provider configuration, ranked discovery, and acquisition.
/// Implementations own external-provider and persistence details.
/// </summary>
public interface ISubtitleAcquisitionService {
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
