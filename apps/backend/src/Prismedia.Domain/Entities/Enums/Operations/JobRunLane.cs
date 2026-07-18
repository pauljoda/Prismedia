namespace Prismedia.Domain.Entities;

/// <summary>
/// Optional foreground lane classification for durable job runs that need worker selection rules
/// beyond broad numeric priority.
/// </summary>
public enum JobRunLane {
    /// <summary>
    /// Direct interactive searches and their lightweight follow-up work, initiated from identify or
    /// acquisition request screens and eligible for the worker's reserved foreground lane. The enum
    /// name and wire code predate acquisition requests and remain stable for durable job history.
    /// </summary>
    [Code("foreground-identify")]
    ForegroundIdentify
}
