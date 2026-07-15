namespace Prismedia.Contracts.Media;

/// <summary>Secret-free configuration state for OpenSubtitles.com.</summary>
public sealed record OpenSubtitlesConfigurationResponse(
    bool Enabled,
    bool ApiKeyConfigured,
    bool UsernameConfigured,
    bool PasswordConfigured,
    bool IncludeAiTranslated,
    bool IncludeMachineTranslated);

/// <summary>Updates OpenSubtitles.com configuration; blank credentials preserve stored values.</summary>
public sealed record UpdateOpenSubtitlesConfigurationRequest(
    bool Enabled,
    string? ApiKey,
    string? Username,
    string? Password,
    bool IncludeAiTranslated,
    bool IncludeMachineTranslated);

/// <summary>Requests subtitle candidates in the listed language priority order.</summary>
public sealed record SearchVideoSubtitlesRequest(IReadOnlyList<string> Languages);

/// <summary>One explainable, ranked provider result.</summary>
public sealed record SubtitleCandidateResponse(
    string Provider,
    string CandidateId,
    string Language,
    string? ReleaseName,
    string Format,
    bool HearingImpaired,
    bool Forced,
    bool AiTranslated,
    bool MachineTranslated,
    bool HashMatched,
    int DownloadCount,
    decimal? Rating,
    int MatchConfidence,
    int QualityScore,
    bool AutomaticEligible,
    IReadOnlyList<string> MatchReasons,
    string? PageUrl);

/// <summary>Ranked subtitle candidates for one video.</summary>
public sealed record SearchVideoSubtitlesResponse(IReadOnlyList<SubtitleCandidateResponse> Candidates);

/// <summary>Acquires one opaque candidate through its server-side provider adapter.</summary>
public sealed record AcquireVideoSubtitleRequest(string Provider, string CandidateId);

/// <summary>Stable track identity created or reused by subtitle acquisition.</summary>
public sealed record AcquireVideoSubtitleResponse(Guid TrackId, bool AlreadyPresent);

/// <summary>Connectivity result for a configured subtitle provider.</summary>
public sealed record SubtitleProviderTestResponse(bool Success, string Message);
