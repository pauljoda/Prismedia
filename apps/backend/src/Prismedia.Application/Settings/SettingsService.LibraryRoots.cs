using Prismedia.Application.Jobs;
using Microsoft.Extensions.Logging;
using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>
/// Watched library-root use cases for <see cref="SettingsService"/>: listing, folder
/// browsing, and create/update/delete of roots (including kicking off the initial scan).
/// </summary>
public sealed partial class SettingsService {
    /// <summary>
    /// Returns the registry catalog plus watched roots for the settings page.
    /// </summary>
    public async Task<LibraryConfigResponse> GetLibraryConfigAsync(CancellationToken cancellationToken) {
        var catalog = await GetCatalogAsync(cancellationToken);
        var roots = await _persistence.ListLibraryRootsAsync(cancellationToken);
        return new LibraryConfigResponse(catalog, roots);
    }

    /// <summary>
    /// Lists every watched library root in stable display order.
    /// </summary>
    public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
        _persistence.ListLibraryRootsAsync(cancellationToken);

    /// <summary>
    /// Lists subdirectories under <paramref name="path"/> for the watched-root folder picker.
    /// Falls back to the user profile directory or the filesystem root when no readable path is
    /// supplied.
    /// </summary>
    public Task<LibraryBrowseResponse> BrowseLibraryPathAsync(string? path, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var requestedPath = string.IsNullOrWhiteSpace(path)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : path;
        var directory = new DirectoryInfo(requestedPath);
        if (!directory.Exists) {
            directory = new DirectoryInfo(Path.GetPathRoot(requestedPath) ?? "/");
        }

        var directories = directory.EnumerateDirectories()
            .Where(child => !child.Attributes.HasFlag(FileAttributes.Hidden))
            .OrderBy(child => child.Name)
            .Select(child => new LibraryBrowseEntry(child.Name, child.FullName))
            .ToArray();

        return Task.FromResult(new LibraryBrowseResponse(
            directory.FullName,
            directory.Parent?.FullName,
            directories));
    }

    /// <summary>
    /// Adds a new watched media root. The label defaults to the trailing directory name when
    /// omitted by the caller, and falls back to the raw path when the directory name is empty.
    /// When the root is enabled, a scan job is queued immediately for each enabled media kind
    /// so newly added libraries begin scanning right away rather than waiting for the optional
    /// recurring auto-scan (which is off by default).
    /// </summary>
    public async Task<LibraryRoot> CreateLibraryRootAsync(
        LibraryRootCreateRequest request,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);

        var now = DateTimeOffset.UtcNow;
        var label = string.IsNullOrWhiteSpace(request.Label)
            ? new DirectoryInfo(request.Path).Name
            : request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label)) {
            label = request.Path;
        }

        var state = new LibraryRoot(
            Id: Guid.NewGuid(),
            Path: request.Path,
            Label: label,
            Enabled: request.Enabled ?? true,
            Recursive: request.Recursive ?? true,
            ScanVideos: request.ScanVideos ?? true,
            ScanImages: request.ScanImages ?? true,
            ScanAudio: request.ScanAudio ?? true,
            ScanBooks: request.ScanBooks ?? false,
            IsNsfw: request.IsNsfw ?? false,
            LastScannedAt: null,
            CreatedAt: now,
            UpdatedAt: now);

        var created = await _persistence.AddLibraryRootAsync(state, cancellationToken);

        if (created.Enabled && _jobs is not null) {
            var queued = await LibraryScanJobs.QueueScansForKindsAsync(
                _jobs,
                created.ScanVideos,
                created.ScanImages,
                created.ScanAudio,
                created.ScanBooks,
                cancellationToken);
            _logger?.LogInformation(
                "Queued {Count} scan job(s) after adding library root '{Label}'.",
                queued, created.Label);
        }

        return created;
    }

    /// <summary>
    /// Partially updates one watched media root. Returns null when no root with the supplied id exists.
    /// </summary>
    public async Task<LibraryRoot?> UpdateLibraryRootAsync(
        Guid id,
        LibraryRootUpdateRequest request,
        CancellationToken cancellationToken) {
        var current = await _persistence.GetLibraryRootAsync(id, cancellationToken);
        if (current is null) {
            return null;
        }

        var next = current with {
            Path = !string.IsNullOrWhiteSpace(request.Path) ? request.Path : current.Path,
            Label = request.Label ?? current.Label,
            Enabled = request.Enabled ?? current.Enabled,
            Recursive = request.Recursive ?? current.Recursive,
            ScanVideos = request.ScanVideos ?? current.ScanVideos,
            ScanImages = request.ScanImages ?? current.ScanImages,
            ScanAudio = request.ScanAudio ?? current.ScanAudio,
            ScanBooks = request.ScanBooks ?? current.ScanBooks,
            IsNsfw = request.IsNsfw ?? current.IsNsfw,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        return await _persistence.SaveLibraryRootAsync(next, cancellationToken);
    }

    /// <summary>
    /// Records that a recurring scan was triggered for one watched media root.
    /// The timestamp marks scheduler intent rather than scan completion.
    /// </summary>
    /// <param name="id">Watched root identifier.</param>
    /// <param name="triggeredAt">UTC time when the scheduler triggered the scan.</param>
    /// <param name="cancellationToken">Token to cancel the persistence operation.</param>
    public async Task<LibraryRoot?> MarkLibraryRootScanTriggeredAsync(
        Guid id,
        DateTimeOffset triggeredAt,
        CancellationToken cancellationToken) {
        var current = await _persistence.GetLibraryRootAsync(id, cancellationToken);
        if (current is null) {
            return null;
        }

        return await _persistence.SaveLibraryRootAsync(current with {
            LastScannedAt = triggeredAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    /// <summary>
    /// Removes one watched media root.
    /// </summary>
    public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
        _persistence.DeleteLibraryRootAsync(id, cancellationToken);
}
