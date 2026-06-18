using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Backups;
using Prismedia.Contracts.Settings;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Database;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class DatabaseBackupServiceTests : IDisposable {
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prismedia-backups-{Guid.NewGuid():N}");

    [Fact]
    public async Task ManualBackupCreatesPermanentCompletedDump() {
        await using var db = CreateContext();
        var process = new DumpProcessExecutor();
        var service = CreateService(db, process);

        var backup = await service.CreateManualBackupAsync(CancellationToken.None);

        Assert.Equal(DatabaseBackupStatus.Completed, backup.Status);
        Assert.True(backup.IsManual);
        Assert.Null(backup.ExpiresAt);
        Assert.True(backup.SizeBytes > 0);
        Assert.True(File.Exists(backup.BackupPath));
        Assert.Equal("pg_dump", process.Calls.Single().FileName);
    }

    [Fact]
    public async Task ManualBackupFallsBackToDockerComposeWhenPgDumpIsMissing() {
        await using var db = CreateContext();
        var process = new DumpProcessExecutor {
            MissingExecutables = { "pg_dump" }
        };
        var service = CreateService(db, process, dockerComposeFilePath: CreateComposeFile());

        var backup = await service.CreateManualBackupAsync(CancellationToken.None);

        Assert.Equal(DatabaseBackupStatus.Completed, backup.Status);
        Assert.True(File.Exists(backup.BackupPath));
        Assert.Collection(
            process.Calls,
            direct => Assert.Equal("pg_dump", direct.FileName),
            lookup => {
                Assert.Equal("docker", lookup.FileName);
                Assert.Contains("container", lookup.Arguments);
                Assert.Contains("ls", lookup.Arguments);
            },
            fallback => {
                Assert.Equal("docker", fallback.FileName);
                Assert.Contains("exec", fallback.Arguments);
                Assert.Contains("pg_dump", fallback.Arguments);
                Assert.Equal(backup.BackupPath, fallback.OutputPath);
            });
    }

    [Fact]
    public async Task AutomaticRetentionDeletesOnlyExpiredAutomaticBackups() {
        await using var db = CreateContext();
        var backupDir = Path.Combine(_tempDir, "database");
        Directory.CreateDirectory(backupDir);
        var expiredPath = Path.Combine(backupDir, "expired.dump");
        var manualPath = Path.Combine(backupDir, "manual.dump");
        await File.WriteAllTextAsync(expiredPath, "expired");
        await File.WriteAllTextAsync(manualPath, "manual");
        var now = DateTimeOffset.UtcNow;

        db.DatabaseBackups.AddRange(
            new DatabaseBackupRow {
                Id = Guid.NewGuid(),
                BackupPath = expiredPath,
                Status = DatabaseBackupStatus.Completed,
                CreatedAt = now.AddDays(-8),
                CompletedAt = now.AddDays(-8),
                ExpiresAt = now.AddDays(-1)
            },
            new DatabaseBackupRow {
                Id = Guid.NewGuid(),
                BackupPath = manualPath,
                Status = DatabaseBackupStatus.Completed,
                IsManual = true,
                CreatedAt = now.AddDays(-30),
                CompletedAt = now.AddDays(-30)
            });
        await db.SaveChangesAsync();

        var service = CreateService(db, new DumpProcessExecutor());
        var pruned = await service.PruneExpiredAutomaticBackupsAsync(CancellationToken.None);

        Assert.Equal(1, pruned);
        Assert.False(File.Exists(expiredPath));
        Assert.True(File.Exists(manualPath));
        Assert.Single(await db.DatabaseBackups.ToListAsync());
    }

    [Fact]
    public async Task RestoreRequiresConfirmationAndStagesMarker() {
        await using var db = CreateContext();
        var backupDir = Path.Combine(_tempDir, "database");
        Directory.CreateDirectory(backupDir);
        var path = Path.Combine(backupDir, "manual.dump");
        await File.WriteAllTextAsync(path, "backup");
        var backupId = Guid.NewGuid();
        db.DatabaseBackups.Add(new DatabaseBackupRow {
            Id = backupId,
            BackupPath = path,
            Status = DatabaseBackupStatus.Completed,
            IsManual = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new DumpProcessExecutor());
        var rejected = await Assert.ThrowsAsync<DatabaseBackupException>(() =>
            service.ScheduleRestoreAsync(backupId, "please", CancellationToken.None));
        Assert.Equal(ApiProblemCodes.DatabaseRestoreInvalid, rejected.ProblemCode);

        var scheduled = await service.ScheduleRestoreAsync(
            backupId,
            DatabaseRestoreConfirmation.Text,
            CancellationToken.None);

        Assert.Equal(backupId, scheduled.BackupId);
        Assert.True(scheduled.RestartScheduled);
        Assert.True(await service.HasPendingRestoreAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PendingRestoreRunsPgRestoreAndClearsMarker() {
        await using var db = CreateContext();
        var backupDir = Path.Combine(_tempDir, "database");
        Directory.CreateDirectory(backupDir);
        var path = Path.Combine(backupDir, "manual.dump");
        await File.WriteAllTextAsync(path, "backup");
        var backupId = Guid.NewGuid();
        db.DatabaseBackups.Add(new DatabaseBackupRow {
            Id = backupId,
            BackupPath = path,
            Status = DatabaseBackupStatus.Completed,
            IsManual = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var process = new DumpProcessExecutor();
        var service = CreateService(db, process);
        await service.ScheduleRestoreAsync(backupId, DatabaseRestoreConfirmation.Text, CancellationToken.None);

        var restored = await service.RunPendingRestoreAsync(CancellationToken.None);

        Assert.True(restored);
        Assert.Equal("pg_restore", Assert.Single(process.Calls).FileName);
        Assert.False(await service.HasPendingRestoreAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PendingRestoreReconcilesRunningRowsFromRestoredSnapshot() {
        await using var db = CreateContext();
        var backupDir = Path.Combine(_tempDir, "database");
        Directory.CreateDirectory(backupDir);
        var path = Path.Combine(backupDir, "manual.dump");
        await File.WriteAllTextAsync(path, "backup");
        var backupId = Guid.NewGuid();
        db.DatabaseBackups.Add(new DatabaseBackupRow {
            Id = backupId,
            BackupPath = path,
            Status = DatabaseBackupStatus.Completed,
            IsManual = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            SizeBytes = 6
        });
        await db.SaveChangesAsync();
        var process = new DumpProcessExecutor {
            OnRunAsync = async call => {
                if (call.FileName != "pg_restore") {
                    return;
                }

                var row = await db.DatabaseBackups.SingleAsync(backup => backup.Id == backupId);
                row.Status = DatabaseBackupStatus.Running;
                row.CompletedAt = null;
                row.SizeBytes = null;
                await db.SaveChangesAsync();
            }
        };
        var service = CreateService(db, process);
        await service.ScheduleRestoreAsync(backupId, DatabaseRestoreConfirmation.Text, CancellationToken.None);

        var restored = await service.RunPendingRestoreAsync(CancellationToken.None);

        Assert.True(restored);
        Assert.False(await service.HasPendingRestoreAsync(CancellationToken.None));
        db.ChangeTracker.Clear();
        var restoredRow = await db.DatabaseBackups.SingleAsync(backup => backup.Id == backupId);
        Assert.Equal(DatabaseBackupStatus.Completed, restoredRow.Status);
        Assert.NotNull(restoredRow.CompletedAt);
        Assert.Equal(new FileInfo(path).Length, restoredRow.SizeBytes);
        Assert.Null(restoredRow.Error);
    }

    [Fact]
    public async Task PendingRestoreFallsBackToDockerComposeWhenPgRestoreIsMissing() {
        await using var db = CreateContext();
        var backupDir = Path.Combine(_tempDir, "database");
        Directory.CreateDirectory(backupDir);
        var path = Path.Combine(backupDir, "manual.dump");
        await File.WriteAllTextAsync(path, "backup");
        var backupId = Guid.NewGuid();
        db.DatabaseBackups.Add(new DatabaseBackupRow {
            Id = backupId,
            BackupPath = path,
            Status = DatabaseBackupStatus.Completed,
            IsManual = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var process = new DumpProcessExecutor {
            MissingExecutables = { "pg_restore" }
        };
        var service = CreateService(db, process, dockerComposeFilePath: CreateComposeFile());
        await service.ScheduleRestoreAsync(backupId, DatabaseRestoreConfirmation.Text, CancellationToken.None);

        var restored = await service.RunPendingRestoreAsync(CancellationToken.None);

        Assert.True(restored);
        Assert.False(await service.HasPendingRestoreAsync(CancellationToken.None));
        Assert.Collection(
            process.Calls,
            direct => Assert.Equal("pg_restore", direct.FileName),
            lookup => {
                Assert.Equal("docker", lookup.FileName);
                Assert.Contains("container", lookup.Arguments);
                Assert.Contains("ls", lookup.Arguments);
            },
            copy => {
                Assert.Equal("docker", copy.FileName);
                Assert.Contains("cp", copy.Arguments);
            },
            restore => {
                Assert.Equal("docker", restore.FileName);
                Assert.Contains("pg_restore", restore.Arguments);
            },
            cleanup => {
                Assert.Equal("docker", cleanup.FileName);
                Assert.Contains("rm", cleanup.Arguments);
            });
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private DatabaseBackupService CreateService(
        PrismediaDbContext db,
        ProcessExecutor process,
        string? dockerComposeFilePath = null) =>
        new(
            db,
            process,
            new DatabaseBackupOptions(
                "Host=localhost;Port=5432;Database=prismedia;Username=prismedia;Password=prismedia",
                Path.Combine(_tempDir, "database"),
                Path.Combine(_tempDir, "database", "restore-request.json"),
                "pg_dump",
                "pg_restore",
                dockerComposeFilePath,
                "postgres",
                7,
                TimeSpan.FromDays(1)),
            NullLogger<DatabaseBackupService>.Instance);

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"database-backups-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private string CreateComposeFile() {
        var composePath = Path.Combine(_tempDir, "docker-compose.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(composePath)!);
        File.WriteAllText(composePath, "services:\n  postgres:\n    image: postgres:16-alpine\n");
        return composePath;
    }

    private sealed class DumpProcessExecutor : ProcessExecutor {
        public HashSet<string> MissingExecutables { get; } = new(StringComparer.Ordinal);
        public List<ProcessCall> Calls { get; } = [];
        public Func<ProcessCall, Task>? OnRunAsync { get; set; }

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken,
            bool lowPriority = false) {
            Calls.Add(new ProcessCall(fileName, arguments.ToArray(), OutputPath: null));
            ThrowIfMissing(fileName);

            if (fileName == "docker" && arguments.Contains("container") && arguments.Contains("ls")) {
                return new ProcessExecutionResult(0, "postgres-container\n", string.Empty);
            }

            if (fileName == "pg_dump") {
                var fileIndex = Array.IndexOf(arguments.ToArray(), "--file");
                if (fileIndex >= 0 && fileIndex + 1 < arguments.Count) {
                    Directory.CreateDirectory(Path.GetDirectoryName(arguments[fileIndex + 1])!);
                    await File.WriteAllTextAsync(arguments[fileIndex + 1], "backup", cancellationToken);
                }
            }

            if (OnRunAsync is not null) {
                await OnRunAsync(Calls[^1]);
            }

            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }

        public override async Task<ProcessExecutionResult> RunToFileAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            string outputPath,
            CancellationToken cancellationToken,
            bool lowPriority = false) {
            Calls.Add(new ProcessCall(fileName, arguments.ToArray(), outputPath));
            ThrowIfMissing(fileName);

            if (fileName == "docker" && arguments.Contains("pg_dump")) {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, "docker backup", cancellationToken);
            }

            return new ProcessExecutionResult(0, string.Empty, string.Empty);
        }

        private void ThrowIfMissing(string fileName) {
            if (MissingExecutables.Contains(fileName)) {
                throw new Win32Exception(2, $"No such file or directory: {fileName}");
            }
        }

        public sealed record ProcessCall(
            string FileName,
            IReadOnlyList<string> Arguments,
            string? OutputPath);
    }
}
