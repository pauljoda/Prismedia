using System.Collections.Concurrent;
using System.IO.Compression;
using Prismedia.Application.Files;
using Prismedia.Contracts.Files;

namespace Prismedia.Infrastructure.Files;

/// <summary>
/// Prepares temporary ZIP archives in the process background and exposes bounded progress snapshots.
/// Prepared files are single-use and stale preparations are removed opportunistically.
/// </summary>
public sealed class FileArchivePreparationService : IFileArchivePreparationService
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<Guid, PreparationState> _preparations = new();
    private readonly string _workingDirectory;

    /// <summary>Creates the archive store under the operating system temporary directory.</summary>
    public FileArchivePreparationService()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "prismedia-file-archives");
        Directory.CreateDirectory(_workingDirectory);
        DeleteStaleFiles();
    }

    /// <inheritdoc />
    public FileArchivePreparation Start(FileArchivePlan plan)
    {
        CleanupExpired();
        var id = Guid.NewGuid();
        var state = new PreparationState(id, plan.FileName, plan.Entries.Count(entry => !entry.IsDirectory));
        _preparations[id] = state;
        _ = Task.Run(() => PrepareAsync(state, plan));
        return state.Snapshot();
    }

    /// <inheritdoc />
    public FileArchivePreparation? Get(Guid id)
    {
        CleanupExpired();
        return _preparations.TryGetValue(id, out var state) ? state.Snapshot() : null;
    }

    /// <inheritdoc />
    public PreparedFileArchive? Claim(Guid id)
    {
        CleanupExpired();
        if (!_preparations.TryGetValue(id, out var state) || !state.Snapshot().Ready)
        {
            return null;
        }

        if (!_preparations.TryRemove(id, out state) || !File.Exists(state.ArchivePath))
        {
            return null;
        }

        return new PreparedFileArchive(id, state.ArchivePath, state.FileName);
    }

    /// <inheritdoc />
    public void Release(PreparedFileArchive archive) => DeleteIfExists(archive.AbsolutePath);

    private async Task PrepareAsync(PreparationState state, FileArchivePlan plan)
    {
        state.ArchivePath = Path.Combine(_workingDirectory, $"{state.Id:N}.zip");
        try
        {
            await using var output = new FileStream(
                state.ArchivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var totalBytes = plan.Entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.SizeBytes);
                long processedBytes = 0;
                foreach (var item in plan.Entries)
                {
                    if (item.IsDirectory)
                    {
                        archive.CreateEntry($"{item.ArchivePath.TrimEnd('/')}/");
                        continue;
                    }

                    var entry = archive.CreateEntry(item.ArchivePath, CompressionLevel.Fastest);
                    await using var source = new FileStream(
                        item.AbsolutePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 81920,
                        useAsync: true);
                    await using var destination = entry.Open();
                    processedBytes += await CopyWithProgressAsync(
                        source,
                        destination,
                        bytes => state.UpdateProgress(processedBytes + bytes, totalBytes));
                    state.FileCompleted(processedBytes, totalBytes);
                }
            }

            state.Complete();
        }
        catch (Exception exception)
        {
            DeleteIfExists(state.ArchivePath);
            state.Fail($"Archive preparation failed: {exception.Message}");
        }
    }

    private static async Task<long> CopyWithProgressAsync(
        Stream source,
        Stream destination,
        Action<long> reportProgress)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            copied += read;
            reportProgress(copied);
        }

        return copied;
    }

    private void CleanupExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var pair in _preparations)
        {
            if (!pair.Value.IsExpired(cutoff) || !_preparations.TryRemove(pair.Key, out var expired))
            {
                continue;
            }

            DeleteIfExists(expired.ArchivePath);
        }
    }

    private void DeleteStaleFiles()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var path in Directory.EnumerateFiles(_workingDirectory, "*.zip"))
        {
            if (File.GetLastWriteTimeUtc(path) < cutoff)
            {
                DeleteIfExists(path);
            }
        }
    }

    private static void DeleteIfExists(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: a later startup/expiry sweep gets another chance.
        }
    }

    private sealed class PreparationState(Guid id, string fileName, int totalFiles)
    {
        private readonly object _gate = new();
        private int _progressPercent;
        private int _processedFiles;
        private bool _ready;
        private string? _error;

        public Guid Id { get; } = id;
        public string FileName { get; } = fileName;
        public int TotalFiles { get; } = totalFiles;
        public string ArchivePath { get; set; } = string.Empty;
        private DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public FileArchivePreparation Snapshot()
        {
            lock (_gate)
            {
                return new FileArchivePreparation(
                    Id,
                    FileName,
                    _ready,
                    _progressPercent,
                    _processedFiles,
                    TotalFiles,
                    _error);
            }
        }

        public bool IsExpired(DateTimeOffset cutoff)
        {
            lock (_gate)
            {
                return UpdatedAt < cutoff;
            }
        }

        public void UpdateProgress(long processedBytes, long totalBytes)
        {
            lock (_gate)
            {
                _progressPercent = totalBytes <= 0
                    ? 0
                    : Math.Min(99, (int)(processedBytes * 100 / totalBytes));
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void FileCompleted(long processedBytes, long totalBytes)
        {
            lock (_gate)
            {
                _processedFiles++;
                _progressPercent = totalBytes <= 0
                    ? Math.Min(99, _processedFiles * 100 / Math.Max(1, TotalFiles))
                    : Math.Min(99, (int)(processedBytes * 100 / totalBytes));
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                _ready = true;
                _progressPercent = 100;
                _processedFiles = TotalFiles;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Fail(string error)
        {
            lock (_gate)
            {
                _error = error;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
