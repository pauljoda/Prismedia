namespace Prismedia.Domain.Entities;

/// <summary>
/// Optional foreground lane classification for durable job runs that need worker selection rules
/// beyond broad numeric priority.
/// </summary>
public enum JobRunLane {
    /// <summary>
    /// Direct manual identify searches and their review cascades, initiated from one entity's
    /// Identify screen and eligible for the worker's reserved foreground lane.
    /// </summary>
    [Code("foreground-identify")]
    ForegroundIdentify
}
