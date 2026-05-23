using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>
/// Application use-case service for shell and library settings. Owns input validation,
/// clamping, default derivation, and the local directory browser. Delegates raw row
/// persistence to <see cref="ISettingsPersistence"/>.
/// </summary>
public sealed class SettingsService {
    private readonly ISettingsPersistence _persistence;

    /// <summary>
    /// Creates the service over the settings persistence port.
    /// </summary>
    /// <param name="persistence">Persistence adapter implemented by Infrastructure.</param>
    public SettingsService(ISettingsPersistence persistence) {
        _persistence = persistence;
    }

    /// <summary>
    /// Returns the small shell-settings subset used by the top-level app chrome.
    /// </summary>
    public async Task<SettingsResponse> GetAsync(CancellationToken cancellationToken) {
        var state = await _persistence.GetLibrarySettingsAsync(cancellationToken);
        return ToShell(state);
    }

    /// <summary>
    /// Applies a partial update to the shell-settings subset and returns the new state.
    /// </summary>
    public async Task<SettingsResponse> UpdateAsync(SettingsUpdateRequest request, CancellationToken cancellationToken) {
        var state = await _persistence.GetLibrarySettingsAsync(cancellationToken);
        var next = state with {
            HideNsfw = request.HideNsfw ?? state.HideNsfw,
            ShowCastControls = request.EnableCastControls ?? state.ShowCastControls,
        };

        var updated = await _persistence.SaveLibrarySettingsAsync(next, cancellationToken);
        return ToShell(updated);
    }

    /// <summary>
    /// Returns the full library settings + watched roots payload for the library settings page.
    /// </summary>
    public async Task<LibraryConfigResponse> GetLibraryConfigAsync(CancellationToken cancellationToken) {
        var settings = await _persistence.GetLibrarySettingsAsync(cancellationToken);
        var roots = await _persistence.ListLibraryRootsAsync(cancellationToken);
        return new LibraryConfigResponse(settings, roots);
    }

    /// <summary>
    /// Applies a partial update to the full library settings record, clamping numeric ranges
    /// and trimming string inputs.
    /// </summary>
    public async Task<LibrarySettings> UpdateLibrarySettingsAsync(
        LibrarySettingsUpdateRequest request,
        CancellationToken cancellationToken) {
        var state = await _persistence.GetLibrarySettingsAsync(cancellationToken);
        var next = ApplyLibraryPatch(state, request);
        return await _persistence.SaveLibrarySettingsAsync(next, cancellationToken);
    }

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
    /// </summary>
    public Task<LibraryRoot> CreateLibraryRootAsync(
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

        return _persistence.AddLibraryRootAsync(state, cancellationToken);
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
    /// Removes one watched media root.
    /// </summary>
    public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) =>
        _persistence.DeleteLibraryRootAsync(id, cancellationToken);

    private static SettingsResponse ToShell(LibrarySettings state) =>
        new(HideNsfw: state.HideNsfw, EnableCastControls: state.ShowCastControls);

    private static LibrarySettings ApplyLibraryPatch(LibrarySettings state, LibrarySettingsUpdateRequest request) =>
        state with {
            AutoScanEnabled = request.AutoScanEnabled ?? state.AutoScanEnabled,
            ScanIntervalMinutes = request.ScanIntervalMinutes is { } scanInterval
                ? Math.Clamp(scanInterval, 5, 1440)
                : state.ScanIntervalMinutes,
            AutoGenerateMetadata = request.AutoGenerateMetadata ?? state.AutoGenerateMetadata,
            AutoGenerateFingerprints = request.AutoGenerateFingerprints ?? state.AutoGenerateFingerprints,
            GeneratePhash = request.GeneratePhash ?? state.GeneratePhash,
            AutoGeneratePreview = request.AutoGeneratePreview ?? state.AutoGeneratePreview,
            GenerateTrickplay = request.GenerateTrickplay ?? state.GenerateTrickplay,
            TrickplayIntervalSeconds = request.TrickplayIntervalSeconds is { } trickplayInterval
                ? Math.Clamp(trickplayInterval, 1, 60)
                : state.TrickplayIntervalSeconds,
            PreviewClipDurationSeconds = request.PreviewClipDurationSeconds is { } previewClip
                ? Math.Clamp(previewClip, 2, 60)
                : state.PreviewClipDurationSeconds,
            ThumbnailQuality = request.ThumbnailQuality is { } thumbnailQuality
                ? Math.Clamp(thumbnailQuality, 1, 5)
                : state.ThumbnailQuality,
            TrickplayQuality = request.TrickplayQuality is { } trickplayQuality
                ? Math.Clamp(trickplayQuality, 1, 5)
                : state.TrickplayQuality,
            BackgroundWorkerConcurrency = request.BackgroundWorkerConcurrency is { } concurrency
                ? Math.Clamp(concurrency, 1, 32)
                : state.BackgroundWorkerConcurrency,
            NsfwLanAutoEnable = request.NsfwLanAutoEnable ?? state.NsfwLanAutoEnable,
            MetadataStorageDedicated = request.MetadataStorageDedicated ?? state.MetadataStorageDedicated,
            SubtitlesAutoEnable = request.SubtitlesAutoEnable ?? state.SubtitlesAutoEnable,
            SubtitlesPreferredLanguages = request.SubtitlesPreferredLanguages ?? state.SubtitlesPreferredLanguages,
            AudioPreferredLanguages = request.AudioPreferredLanguages ?? state.AudioPreferredLanguages,
            SubtitleStyle = request.SubtitleStyle ?? state.SubtitleStyle,
            SubtitleFontScale = request.SubtitleFontScale is { } subtitleFontScale
                ? Math.Clamp(subtitleFontScale, 0.5f, 3f)
                : state.SubtitleFontScale,
            SubtitlePositionPercent = request.SubtitlePositionPercent is { } subtitlePosition
                ? Math.Clamp(subtitlePosition, 0f, 100f)
                : state.SubtitlePositionPercent,
            SubtitleOpacity = request.SubtitleOpacity is { } subtitleOpacity
                ? Math.Clamp(subtitleOpacity, 0.2f, 1f)
                : state.SubtitleOpacity,
            DefaultPlaybackMode = request.DefaultPlaybackMode ?? state.DefaultPlaybackMode,
            ShowCastControls = request.ShowCastControls ?? state.ShowCastControls,
            HlsTranscoderProfile = request.HlsTranscoderProfile ?? state.HlsTranscoderProfile,
            HlsFfmpegPath = NormalizeOptionalPath(request.HlsFfmpegPath, state.HlsFfmpegPath),
            HlsVaapiDevice = NormalizeOptionalPath(request.HlsVaapiDevice, state.HlsVaapiDevice),
        };

    private static string NormalizeOptionalPath(string? value, string current) {
        if (value is null) {
            return current;
        }

        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? current : trimmed;
    }
}
