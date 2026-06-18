using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Prismedia.Application.Backups;
using Prismedia.Contracts.Settings;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Database;

/// <summary>
/// Postgres-backed implementation of Prismedia database backups.
/// </summary>
public sealed class DatabaseBackupService(
    PrismediaDbContext db,
    ProcessExecutor processes,
    DatabaseBackupOptions options,
    ILogger<DatabaseBackupService> logger,
    TimeProvider? timeProvider = null) : IDatabaseBackupService {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim BackupGate = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DatabaseBackupListResponse> ListAsync(CancellationToken cancellationToken) {
        await PruneExpiredAutomaticBackupsAsync(cancellationToken);

        var rows = await db.DatabaseBackups
            .AsNoTracking()
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);

        return new DatabaseBackupListResponse(
            rows.Select(ToDto).ToList(),
            NextAutomaticBackupAt(rows),
            options.BackupDirectory,
            options.AutomaticRetentionDays,
            DatabaseRestoreConfirmation.Text);
    }

    public Task<DatabaseBackupDto> CreateManualBackupAsync(CancellationToken cancellationToken) =>
        CreateBackupAsync(isManual: true, cancellationToken);

    public Task<DatabaseBackupDto> CreateAutomaticBackupAsync(CancellationToken cancellationToken) =>
        CreateBackupAsync(isManual: false, cancellationToken);

    public async Task<bool> IsAutomaticBackupDueAsync(CancellationToken cancellationToken) {
        var running = await db.DatabaseBackups
            .AsNoTracking()
            .AnyAsync(row => !row.IsManual && row.Status == DatabaseBackupStatus.Running, cancellationToken);
        if (running) {
            return false;
        }

        var lastCompleted = await db.DatabaseBackups
            .AsNoTracking()
            .Where(row => !row.IsManual && row.Status == DatabaseBackupStatus.Completed)
            .OrderByDescending(row => row.CompletedAt ?? row.CreatedAt)
            .Select(row => (DateTimeOffset?)(row.CompletedAt ?? row.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return lastCompleted is null || _timeProvider.GetUtcNow() - lastCompleted >= options.AutomaticInterval;
    }

    public async Task<int> PruneExpiredAutomaticBackupsAsync(CancellationToken cancellationToken) {
        var now = _timeProvider.GetUtcNow();
        var expired = await db.DatabaseBackups
            .Where(row => !row.IsManual && row.ExpiresAt != null && row.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0) {
            return 0;
        }

        foreach (var row in expired) {
            DeleteBackupFile(row.BackupPath);
            db.DatabaseBackups.Remove(row);
        }

        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    public async Task<DatabaseRestoreScheduledResponse> ScheduleRestoreAsync(
        Guid backupId,
        string confirmationText,
        CancellationToken cancellationToken) {
        if (!string.Equals(
                confirmationText,
                DatabaseRestoreConfirmation.Text,
                StringComparison.Ordinal)) {
            throw new DatabaseBackupException(
                ApiProblemCodes.DatabaseRestoreInvalid,
                $"Type {DatabaseRestoreConfirmation.Text} to confirm database restore.");
        }

        var row = await db.DatabaseBackups
            .AsNoTracking()
            .FirstOrDefaultAsync(backup => backup.Id == backupId, cancellationToken);
        if (row is null) {
            throw new DatabaseBackupException(
                ApiProblemCodes.DatabaseBackupNotFound,
                $"Database backup '{backupId}' was not found.");
        }

        if (row.Status != DatabaseBackupStatus.Completed) {
            throw new DatabaseBackupException(
                ApiProblemCodes.DatabaseBackupInvalid,
                "Only completed backups can be restored.");
        }

        if (!File.Exists(row.BackupPath)) {
            throw new DatabaseBackupException(
                ApiProblemCodes.DatabaseBackupInvalid,
                "The selected backup file no longer exists on disk.");
        }

        var requestedAt = _timeProvider.GetUtcNow();
        var request = new DatabaseRestoreRequestFile(row.Id, row.BackupPath, requestedAt);
        Directory.CreateDirectory(Path.GetDirectoryName(options.RestoreRequestPath)!);
        await File.WriteAllTextAsync(
            options.RestoreRequestPath,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);

        return new DatabaseRestoreScheduledResponse(row.Id, requestedAt, RestartScheduled: true);
    }

    public Task<bool> HasPendingRestoreAsync(CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(options.RestoreRequestPath));

    public async Task<DatabaseRestoreStatusResponse> GetRestoreStatusAsync(CancellationToken cancellationToken) {
        var failedPath = FailedRestoreRequestPath();
        var errorPath = $"{failedPath}.error";
        var error = File.Exists(errorPath)
            ? await File.ReadAllTextAsync(errorPath, cancellationToken)
            : null;

        return new DatabaseRestoreStatusResponse(
            RestorePending: File.Exists(options.RestoreRequestPath),
            RestoreFailed: File.Exists(failedPath),
            Error: string.IsNullOrWhiteSpace(error) ? null : error);
    }

    public async Task<bool> RunPendingRestoreAsync(CancellationToken cancellationToken) {
        if (!File.Exists(options.RestoreRequestPath)) {
            return false;
        }

        await BackupGate.WaitAsync(cancellationToken);
        try {
            if (!File.Exists(options.RestoreRequestPath)) {
                return false;
            }

            DatabaseRestoreRequestFile request;
            try {
                var json = await File.ReadAllTextAsync(options.RestoreRequestPath, cancellationToken);
                request = JsonSerializer.Deserialize<DatabaseRestoreRequestFile>(json, JsonOptions)
                    ?? throw new InvalidOperationException("Restore request file is empty.");
            } catch (Exception ex) {
                MoveRestoreRequestAside(ex);
                throw new DatabaseBackupException(
                    ApiProblemCodes.DatabaseRestoreInvalid,
                    "The pending restore request file could not be read.");
            }

            var backupPath = Path.GetFullPath(request.BackupPath);
            if (!IsPathUnderDirectory(backupPath, options.BackupDirectory) || !File.Exists(backupPath)) {
                MoveRestoreRequestAside();
                throw new DatabaseBackupException(
                    ApiProblemCodes.DatabaseBackupInvalid,
                    "The pending restore backup file is missing or outside the backup directory.");
            }

            logger.LogWarning("Restoring Prismedia database from {BackupPath}.", backupPath);
            NpgsqlConnection.ClearAllPools();

            try {
                await RunPgRestoreAsync(backupPath, cancellationToken);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                MoveRestoreRequestAside(ex);
                throw new DatabaseBackupException(
                    ApiProblemCodes.DatabaseRestoreInvalid,
                    $"Database restore failed: {ex.Message}");
            }

            File.Delete(options.RestoreRequestPath);
            logger.LogWarning("Prismedia database restore completed from {BackupPath}.", backupPath);
            return true;
        } finally {
            BackupGate.Release();
        }
    }

    private async Task<DatabaseBackupDto> CreateBackupAsync(bool isManual, CancellationToken cancellationToken) {
        await BackupGate.WaitAsync(cancellationToken);
        try {
            Directory.CreateDirectory(options.BackupDirectory);

            var now = _timeProvider.GetUtcNow();
            var id = Guid.NewGuid();
            var fileName = $"{(isManual ? "prismedia-manual" : "prismedia-auto")}-{now:yyyyMMddTHHmmssZ}-{id:N}.dump";
            var backupPath = Path.Combine(options.BackupDirectory, fileName);
            var row = new DatabaseBackupRow {
                Id = id,
                BackupPath = backupPath,
                Status = DatabaseBackupStatus.Running,
                IsManual = isManual,
                CreatedAt = now,
                ExpiresAt = isManual ? null : now.AddDays(options.AutomaticRetentionDays)
            };

            db.DatabaseBackups.Add(row);
            await db.SaveChangesAsync(cancellationToken);

            try {
                await RunPgDumpAsync(backupPath, isManual, cancellationToken);

                row.Status = DatabaseBackupStatus.Completed;
                row.CompletedAt = _timeProvider.GetUtcNow();
                row.SizeBytes = File.Exists(backupPath) ? new FileInfo(backupPath).Length : null;
                row.Error = null;
                await db.SaveChangesAsync(cancellationToken);
                return ToDto(row);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                DeleteBackupFile(backupPath);
                row.Status = DatabaseBackupStatus.Failed;
                row.CompletedAt = _timeProvider.GetUtcNow();
                row.SizeBytes = null;
                row.Error = ex.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                throw new DatabaseBackupException(
                    ApiProblemCodes.DatabaseBackupInvalid,
                    $"Database backup failed: {ex.Message}");
            }
        } finally {
            BackupGate.Release();
        }
    }

    private async Task RunPgDumpAsync(
        string backupPath,
        bool isManual,
        CancellationToken cancellationToken) {
        try {
            var result = await processes.RunAsync(
                options.PgDumpPath,
                [
                    "--format=custom",
                    "--no-owner",
                    "--no-acl",
                    "--file",
                    backupPath,
                    DatabaseName()
                ],
                BuildPostgresEnvironment(),
                cancellationToken,
                lowPriority: !isManual);
            ThrowIfProcessFailed(result, "pg_dump");
        } catch (Exception ex) when (IsExecutableMissing(ex)) {
            if (!CanUseDockerComposePostgresClient()) {
                throw MissingPostgresClientException(options.PgDumpPath, "PRISMEDIA_PG_DUMP_PATH", ex);
            }

            logger.LogInformation(
                "Postgres backup tool {ToolPath} was not found; falling back to Docker Compose Postgres service {ServiceName}.",
                options.PgDumpPath,
                options.DockerComposePostgresService);
            await RunDockerPgDumpAsync(backupPath, isManual, cancellationToken);
        }
    }

    private async Task RunPgRestoreAsync(string backupPath, CancellationToken cancellationToken) {
        try {
            var result = await processes.RunAsync(
                options.PgRestorePath,
                [
                    "--clean",
                    "--if-exists",
                    "--no-owner",
                    "--no-acl",
                    "--dbname",
                    DatabaseName(),
                    backupPath
                ],
                BuildPostgresEnvironment(),
                cancellationToken);
            ThrowIfProcessFailed(result, "pg_restore");
        } catch (Exception ex) when (IsExecutableMissing(ex)) {
            if (!CanUseDockerComposePostgresClient()) {
                throw MissingPostgresClientException(options.PgRestorePath, "PRISMEDIA_PG_RESTORE_PATH", ex);
            }

            logger.LogInformation(
                "Postgres restore tool {ToolPath} was not found; falling back to Docker Compose Postgres service {ServiceName}.",
                options.PgRestorePath,
                options.DockerComposePostgresService);
            await RunDockerPgRestoreAsync(backupPath, cancellationToken);
        }
    }

    private async Task RunDockerPgDumpAsync(
        string backupPath,
        bool isManual,
        CancellationToken cancellationToken) {
        var containerId = await ResolveDockerPostgresContainerAsync(cancellationToken);
        var result = await RunDockerToFileAsync(
            BuildDockerPgDumpArguments(containerId),
            backupPath,
            cancellationToken,
            lowPriority: !isManual);
        ThrowIfProcessFailed(result, "docker exec pg_dump");
    }

    private async Task RunDockerPgRestoreAsync(string backupPath, CancellationToken cancellationToken) {
        var containerId = await ResolveDockerPostgresContainerAsync(cancellationToken);
        var tempPath = $"/tmp/prismedia-restore-{Guid.NewGuid():N}.dump";
        var copied = false;

        try {
            var copyResult = await RunDockerAsync(
                BuildDockerCopyArguments(containerId, backupPath, tempPath),
                cancellationToken);
            ThrowIfProcessFailed(copyResult, "docker cp");
            copied = true;

            var restoreResult = await RunDockerAsync(
                BuildDockerPgRestoreArguments(containerId, tempPath),
                cancellationToken);
            ThrowIfProcessFailed(restoreResult, "docker exec pg_restore");
        } finally {
            if (copied) {
                await CleanupDockerTempFileAsync(containerId, tempPath);
            }
        }
    }

    private async Task CleanupDockerTempFileAsync(string containerId, string tempPath) {
        try {
            var result = await RunDockerAsync(BuildDockerCleanupArguments(containerId, tempPath), CancellationToken.None);
            if (result.ExitCode != 0) {
                logger.LogWarning(
                    "Could not remove temporary database restore file {TempPath}: {Error}",
                    tempPath,
                    TrimProcessError(result.StandardError));
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Could not remove temporary database restore file {TempPath}.", tempPath);
        }
    }

    private async Task<string> ResolveDockerPostgresContainerAsync(CancellationToken cancellationToken) {
        var result = await RunDockerAsync(BuildDockerContainerListArguments(), cancellationToken);
        ThrowIfProcessFailed(result, "docker container ls");

        var containers = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (containers.Length == 0) {
            throw new InvalidOperationException(
                $"Docker Compose Postgres service '{options.DockerComposePostgresService}' is not running.");
        }

        if (containers.Length > 1) {
            logger.LogWarning(
                "Found {Count} Docker containers for Postgres service {ServiceName}; using {ContainerId}.",
                containers.Length,
                options.DockerComposePostgresService,
                containers[0]);
        }

        return containers[0];
    }

    private async Task<ProcessExecutionResult> RunDockerAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) {
        try {
            return await processes.RunAsync("docker", arguments, null, cancellationToken);
        } catch (Exception ex) when (IsExecutableMissing(ex)) {
            throw DockerMissingException(ex);
        }
    }

    private async Task<ProcessExecutionResult> RunDockerToFileAsync(
        IReadOnlyList<string> arguments,
        string outputPath,
        CancellationToken cancellationToken,
        bool lowPriority) {
        try {
            return await processes.RunToFileAsync(
                "docker",
                arguments,
                null,
                outputPath,
                cancellationToken,
                lowPriority);
        } catch (Exception ex) when (IsExecutableMissing(ex)) {
            throw DockerMissingException(ex);
        }
    }

    private IReadOnlyList<string> BuildDockerPgDumpArguments(string containerId) {
        var arguments = BuildDockerExecArguments(containerId, interactive: true);
        arguments.Add("pg_dump");
        arguments.Add("--format=custom");
        arguments.Add("--no-owner");
        arguments.Add("--no-acl");
        AddPostgresUserArgument(arguments);
        arguments.Add("--dbname");
        arguments.Add(DatabaseName());
        return arguments;
    }

    private IReadOnlyList<string> BuildDockerPgRestoreArguments(string containerId, string tempPath) {
        var arguments = BuildDockerExecArguments(containerId, interactive: true);
        arguments.Add("pg_restore");
        arguments.Add("--clean");
        arguments.Add("--if-exists");
        arguments.Add("--no-owner");
        arguments.Add("--no-acl");
        AddPostgresUserArgument(arguments);
        arguments.Add("--dbname");
        arguments.Add(DatabaseName());
        arguments.Add(tempPath);
        return arguments;
    }

    private IReadOnlyList<string> BuildDockerCopyArguments(string containerId, string backupPath, string tempPath) =>
        [
            "cp",
            backupPath,
            $"{containerId}:{tempPath}"
        ];

    private IReadOnlyList<string> BuildDockerCleanupArguments(string containerId, string tempPath) =>
        [
            "exec",
            containerId,
            "rm",
            "-f",
            tempPath
        ];

    private IReadOnlyList<string> BuildDockerContainerListArguments() =>
        [
            "container",
            "ls",
            "-q",
            "--filter",
            $"label=com.docker.compose.service={options.DockerComposePostgresService}",
            "--filter",
            $"label=com.docker.compose.project.config_files={Path.GetFullPath(options.DockerComposeFilePath!)}"
        ];

    private List<string> BuildDockerExecArguments(string containerId, bool interactive) {
        var arguments = new List<string> {
            "exec"
        };
        if (interactive) {
            arguments.Add("-i");
        }

        foreach (var (key, value) in BuildDockerPostgresEnvironment()) {
            arguments.Add("--env");
            arguments.Add($"{key}={value}");
        }

        arguments.Add(containerId);
        return arguments;
    }

    private IReadOnlyDictionary<string, string> BuildDockerPostgresEnvironment() {
        var env = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["PGDATABASE"] = DatabaseName()
        };
        var direct = BuildPostgresEnvironment();
        if (direct.TryGetValue("PGUSER", out var username)) {
            env["PGUSER"] = username;
        }

        if (direct.TryGetValue("PGPASSWORD", out var password)) {
            env["PGPASSWORD"] = password;
        }

        return env;
    }

    private void AddPostgresUserArgument(List<string> arguments) {
        var direct = BuildPostgresEnvironment();
        if (!direct.TryGetValue("PGUSER", out var username) || string.IsNullOrWhiteSpace(username)) {
            return;
        }

        arguments.Add("--username");
        arguments.Add(username);
    }

    private bool CanUseDockerComposePostgresClient() =>
        !string.IsNullOrWhiteSpace(options.DockerComposeFilePath) &&
        !string.IsNullOrWhiteSpace(options.DockerComposePostgresService) &&
        File.Exists(options.DockerComposeFilePath) &&
        IsLocalDatabaseHost();

    private bool IsLocalDatabaseHost() {
        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        var hosts = (builder.Host ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return hosts.Length == 0 || hosts.All(IsLocalDatabaseHost);
    }

    private static bool IsLocalDatabaseHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
        string.Equals(host, "::1", StringComparison.Ordinal) ||
        string.Equals(host, "[::1]", StringComparison.Ordinal);

    private static void ThrowIfProcessFailed(ProcessExecutionResult result, string commandName) {
        if (result.ExitCode != 0) {
            throw new InvalidOperationException($"{commandName} failed: {TrimProcessError(result.StandardError)}");
        }
    }

    private static bool IsExecutableMissing(Exception ex) =>
        ex is Win32Exception { NativeErrorCode: 2 } ||
        ex is FileNotFoundException ||
        ex.Message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("start", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException MissingPostgresClientException(
        string toolPath,
        string configurationKey,
        Exception innerException) =>
        new(
            $"PostgreSQL client tool '{toolPath}' was not found. Install PostgreSQL client tools or set {configurationKey} to the executable path.",
            innerException);

    private static InvalidOperationException DockerMissingException(Exception innerException) =>
        new(
            "Docker Compose fallback could not start because the 'docker' command was not found. Install PostgreSQL client tools or Docker Desktop.",
            innerException);

    private DatabaseBackupDto ToDto(DatabaseBackupRow row) {
        var fileName = Path.GetFileName(row.BackupPath);
        var size = File.Exists(row.BackupPath)
            ? new FileInfo(row.BackupPath).Length
            : row.SizeBytes;

        return new DatabaseBackupDto(
            row.Id,
            fileName,
            row.BackupPath,
            row.Status,
            row.IsManual,
            size,
            row.CreatedAt,
            row.CompletedAt,
            row.ExpiresAt,
            row.Error);
    }

    private DateTimeOffset? NextAutomaticBackupAt(IReadOnlyList<DatabaseBackupRow> rows) {
        if (rows.Any(row => !row.IsManual && row.Status == DatabaseBackupStatus.Running)) {
            return null;
        }

        var lastCompleted = rows
            .Where(row => !row.IsManual && row.Status == DatabaseBackupStatus.Completed)
            .Select(row => (DateTimeOffset?)(row.CompletedAt ?? row.CreatedAt))
            .OrderByDescending(value => value)
            .FirstOrDefault();

        return lastCompleted is null
            ? _timeProvider.GetUtcNow()
            : lastCompleted.Value.Add(options.AutomaticInterval);
    }

    private IReadOnlyDictionary<string, string> BuildPostgresEnvironment() {
        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        var env = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["PGHOST"] = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host,
            ["PGPORT"] = builder.Port > 0 ? builder.Port.ToString() : "5432",
            ["PGDATABASE"] = DatabaseName()
        };

        if (!string.IsNullOrWhiteSpace(builder.Username)) {
            env["PGUSER"] = builder.Username;
        }

        if (!string.IsNullOrWhiteSpace(builder.Password)) {
            env["PGPASSWORD"] = builder.Password;
        }

        return env;
    }

    private string DatabaseName() {
        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        if (string.IsNullOrWhiteSpace(builder.Database)) {
            throw new InvalidOperationException("Database backup requires a database name.");
        }

        return builder.Database;
    }

    private void MoveRestoreRequestAside(Exception? ex = null) {
        var failedPath = FailedRestoreRequestPath();
        if (File.Exists(failedPath)) {
            File.Delete(failedPath);
        }

        File.Move(options.RestoreRequestPath, failedPath);
        if (ex is not null) {
            File.WriteAllText($"{failedPath}.error", ex.Message);
        }
    }

    private string FailedRestoreRequestPath() => $"{options.RestoreRequestPath}.failed";

    private static bool IsPathUnderDirectory(string path, string directory) {
        var root = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(root, path);
        return relative != "." &&
               !relative.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static void DeleteBackupFile(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (IOException) {
            // Backup retention should not fail the settings page because an old file is busy.
        } catch (UnauthorizedAccessException) {
            // The row is still removed on best-effort cleanup; the file can be removed by an operator later.
        }
    }

    private static string TrimProcessError(string? standardError) {
        var message = standardError?.Trim();
        return string.IsNullOrWhiteSpace(message)
            ? "pg_dump or pg_restore exited unsuccessfully."
            : message.Length > 1_000
                ? message[..1_000]
                : message;
    }

    private sealed record DatabaseRestoreRequestFile(
        Guid BackupId,
        string BackupPath,
        DateTimeOffset RequestedAt);
}
