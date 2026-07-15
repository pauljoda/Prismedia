namespace Prismedia.Application.Subtitles;

/// <summary>Stable identifiers for built-in subtitle providers.</summary>
public static class SubtitleProviderCodes {
    public const string OpenSubtitles = "opensubtitles";
}

/// <summary>Credential keys stored for the OpenSubtitles provider.</summary>
public static class OpenSubtitlesCredentialKeys {
    public const string ApiKey = "apiKey";
    public const string Username = "username";
    public const string Password = "password";
}

/// <summary>Safe, secret-free OpenSubtitles configuration exposed to clients.</summary>
public sealed record OpenSubtitlesConfiguration(
    bool Enabled,
    bool ApiKeyConfigured,
    bool UsernameConfigured,
    bool PasswordConfigured,
    bool IncludeAiTranslated,
    bool IncludeMachineTranslated);

/// <summary>Configuration mutation for the built-in OpenSubtitles provider.</summary>
public sealed record SaveOpenSubtitlesConfiguration(
    bool Enabled,
    string? ApiKey,
    string? Username,
    string? Password,
    bool IncludeAiTranslated,
    bool IncludeMachineTranslated);

/// <summary>Search parameters supplied by the transcript experience.</summary>
public sealed record SubtitleSearchRequest(IReadOnlyList<string> Languages);

/// <summary>A provider result ranked against one local video.</summary>
public sealed record SubtitleSearchResult(
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

/// <summary>Result of importing one provider subtitle into Prismedia-owned assets.</summary>
public sealed record SubtitleAcquisitionResult(Guid TrackId, bool AlreadyPresent);

/// <summary>Result of one automatic preferred-language acquisition pass.</summary>
public sealed record AutomaticSubtitleAcquisitionResult(
    int DownloadedCount,
    IReadOnlyList<string> MissingLanguages,
    string? SkipReason = null);

/// <summary>Provider connectivity result suitable for a settings surface.</summary>
public sealed record SubtitleProviderTestResult(bool Success, string Message);

/// <summary>Raised when a configured provider cannot complete a search or download.</summary>
public sealed class SubtitleProviderUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Raised when a selected opaque provider candidate expired or disappeared.</summary>
public sealed class SubtitleCandidateUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Raised when downloaded subtitle bytes cannot be safely normalized and published.</summary>
public sealed class SubtitleImportException(string message, Exception? innerException = null)
    : Exception(message, innerException);
