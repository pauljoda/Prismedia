using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Backups;

/// <summary>
/// Coordinates Prismedia database backup creation, retention, and restore staging.
/// </summary>
public interface IDatabaseBackupService {
    /// <summary>
    /// Lists tracked database backups and scheduling metadata for the settings UI.
    /// </summary>
    Task<DatabaseBackupListResponse> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a user-requested permanent database backup immediately.
    /// </summary>
    Task<DatabaseBackupDto> CreateManualBackupAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a retained automatic database backup immediately.
    /// </summary>
    Task<DatabaseBackupDto> CreateAutomaticBackupAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the daily automatic backup window is due and no automatic backup is in flight.
    /// </summary>
    Task<bool> IsAutomaticBackupDueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired automatic backup rows and files while preserving manual backups.
    /// </summary>
    Task<int> PruneExpiredAutomaticBackupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stages a destructive restore request to be applied during the next API startup.
    /// </summary>
    Task<DatabaseRestoreScheduledResponse> ScheduleRestoreAsync(
        Guid backupId,
        string confirmationText,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a pending restore marker if one exists.
    /// </summary>
    Task<bool> RunPendingRestoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads pending or failed restore marker state for status surfaces.
    /// </summary>
    Task<DatabaseRestoreStatusResponse> GetRestoreStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns true while a restore marker is waiting for the API startup restore pass.
    /// </summary>
    Task<bool> HasPendingRestoreAsync(CancellationToken cancellationToken);
}

/// <summary>
/// User-facing backup or restore validation failure.
/// </summary>
public sealed class DatabaseBackupException : Exception {
    /// <summary>
    /// Creates a backup exception with a canonical API problem code.
    /// </summary>
    public DatabaseBackupException(string problemCode, string message) : base(message) {
        ProblemCode = problemCode;
    }

    /// <summary>
    /// Canonical machine-readable API problem code.
    /// </summary>
    public string ProblemCode { get; }
}
