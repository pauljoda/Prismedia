namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of database backup lifecycle statuses.
/// </summary>
public enum DatabaseBackupStatus {
    /// <summary>Backup process is currently running.</summary>
    [Code("running")]
    Running,

    /// <summary>Backup finished and the output file is ready.</summary>
    [Code("completed")]
    Completed,

    /// <summary>Backup process failed and the error field should explain why.</summary>
    [Code("failed")]
    Failed
}
