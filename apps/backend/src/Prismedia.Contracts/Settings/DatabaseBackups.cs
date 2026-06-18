using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Settings;

/// <summary>
/// One tracked database backup file available from the settings page.
/// </summary>
/// <param name="Id">Stable backup row identifier.</param>
/// <param name="FileName">Display file name for the dump on disk.</param>
/// <param name="BackupPath">Absolute path inside the Prismedia data volume.</param>
/// <param name="Status">Current backup lifecycle status.</param>
/// <param name="IsManual">True for user-created permanent backups; false for retained automatic backups.</param>
/// <param name="SizeBytes">Backup file size in bytes, when the file exists.</param>
/// <param name="CreatedAt">UTC time the backup row was created.</param>
/// <param name="CompletedAt">UTC time the dump finished, when successful.</param>
/// <param name="ExpiresAt">UTC expiry for automatic backups, or null for permanent manual backups.</param>
/// <param name="Error">Failure message for failed backup attempts.</param>
public sealed record DatabaseBackupDto(
    Guid Id,
    string FileName,
    string BackupPath,
    DatabaseBackupStatus Status,
    bool IsManual,
    long? SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    string? Error);

/// <summary>
/// Database backup settings card state.
/// </summary>
/// <param name="Backups">Known backups ordered newest first.</param>
/// <param name="NextAutomaticBackupAt">UTC time the next automatic backup is due, when known.</param>
/// <param name="BackupDirectory">Directory that stores Prismedia database dumps.</param>
/// <param name="AutomaticRetentionDays">Number of days automatic backups are retained.</param>
/// <param name="RestoreConfirmationText">Exact confirmation phrase required before scheduling a restore.</param>
public sealed record DatabaseBackupListResponse(
    IReadOnlyList<DatabaseBackupDto> Backups,
    DateTimeOffset? NextAutomaticBackupAt,
    string BackupDirectory,
    int AutomaticRetentionDays,
    string RestoreConfirmationText);

/// <summary>
/// Request body for scheduling a destructive database restore.
/// </summary>
/// <param name="BackupId">Tracked backup to restore on restart.</param>
/// <param name="ConfirmationText">Exact confirmation phrase proving the caller accepted the destructive restore.</param>
public sealed record DatabaseRestoreRequest(Guid BackupId, string ConfirmationText);

/// <summary>
/// Result returned after a restore request has been staged.
/// </summary>
/// <param name="BackupId">Backup that will be restored.</param>
/// <param name="RequestedAt">UTC time the restore was scheduled.</param>
/// <param name="RestartScheduled">True when Prismedia will stop so its supervisor can restart and restore.</param>
public sealed record DatabaseRestoreScheduledResponse(
    Guid BackupId,
    DateTimeOffset RequestedAt,
    bool RestartScheduled);

/// <summary>
/// Current destructive database restore status.
/// </summary>
/// <param name="RestorePending">True while a restore marker is waiting to be applied.</param>
/// <param name="RestoreFailed">True when the last pending restore marker was moved aside as failed.</param>
/// <param name="Error">Failure details from the moved-aside restore marker, when available.</param>
public sealed record DatabaseRestoreStatusResponse(
    bool RestorePending,
    bool RestoreFailed,
    string? Error);

/// <summary>
/// Shared destructive restore confirmation phrase.
/// </summary>
public static class DatabaseRestoreConfirmation {
    /// <summary>Exact phrase required by the API before a database restore can be staged.</summary>
    public const string Text = "DESTROY AND RESTORE";
}
