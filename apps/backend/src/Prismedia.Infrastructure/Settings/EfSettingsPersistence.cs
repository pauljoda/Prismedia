using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Settings;

/// <summary>
/// EF Core adapter for <see cref="ISettingsPersistence"/>. Owns the row ↔ Contract DTO
/// translation for both the singleton <c>library_settings</c> row and the watched library
/// roots, and normalizes the HLS transcoder profile string on persist so callers downstream
/// always see a value that maps to a supported encoder.
/// </summary>
public sealed class EfSettingsPersistence : ISettingsPersistence {
    private readonly PrismediaDbContext _db;

    public EfSettingsPersistence(PrismediaDbContext db) {
        _db = db;
    }

    public async Task<LibrarySettings> GetLibrarySettingsAsync(CancellationToken cancellationToken) {
        var row = await EnsureRowAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<LibrarySettings> SaveLibrarySettingsAsync(
        LibrarySettings state,
        CancellationToken cancellationToken) {
        var row = await EnsureRowAsync(cancellationToken);
        ApplyToRow(row, state);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) {
        return await _db.LibraryRoots
            .AsNoTracking()
            .OrderBy(root => root.Label)
            .ThenBy(root => root.Path)
            .Select(root => ToContract(root))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.AsNoTracking().FirstOrDefaultAsync(root => root.Id == id, cancellationToken);
        return row is null ? null : ToContract(row);
    }

    public async Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var row = new LibraryRootRow {
            Id = state.Id,
            Path = state.Path,
            Label = state.Label,
            Enabled = state.Enabled,
            Recursive = state.Recursive,
            ScanVideos = state.ScanVideos,
            ScanImages = state.ScanImages,
            ScanAudio = state.ScanAudio,
            ScanBooks = state.ScanBooks,
            IsNsfw = state.IsNsfw,
            LastScannedAt = state.LastScannedAt,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt,
        };

        _db.LibraryRoots.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([state.Id], cancellationToken)
            ?? throw new InvalidOperationException($"Library root '{state.Id}' was not found.");

        row.Path = state.Path;
        row.Label = state.Label;
        row.Enabled = state.Enabled;
        row.Recursive = state.Recursive;
        row.ScanVideos = state.ScanVideos;
        row.ScanImages = state.ScanImages;
        row.ScanAudio = state.ScanAudio;
        row.ScanBooks = state.ScanBooks;
        row.IsNsfw = state.IsNsfw;
        row.LastScannedAt = state.LastScannedAt;
        row.UpdatedAt = state.UpdatedAt;

        await _db.SaveChangesAsync(cancellationToken);
        return ToContract(row);
    }

    public async Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) {
        var row = await _db.LibraryRoots.FindAsync([id], cancellationToken);
        if (row is null) {
            return false;
        }

        _db.LibraryRoots.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<LibrarySettingsRow> EnsureRowAsync(CancellationToken cancellationToken) {
        var row = await _db.LibrarySettings
            .OrderBy(settings => settings.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is not null) {
            return row;
        }

        var now = DateTimeOffset.UtcNow;
        row = new LibrarySettingsRow {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.LibrarySettings.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private static void ApplyToRow(LibrarySettingsRow row, LibrarySettings state) {
        row.AutoScanEnabled = state.AutoScanEnabled;
        row.ScanIntervalMinutes = state.ScanIntervalMinutes;
        row.AutoGenerateMetadata = state.AutoGenerateMetadata;
        row.AutoGenerateFingerprints = state.AutoGenerateFingerprints;
        row.GeneratePhash = state.GeneratePhash;
        row.AutoGeneratePreview = state.AutoGeneratePreview;
        row.GenerateTrickplay = state.GenerateTrickplay;
        row.TrickplayIntervalSeconds = state.TrickplayIntervalSeconds;
        row.PreviewClipDurationSeconds = state.PreviewClipDurationSeconds;
        row.ThumbnailQuality = state.ThumbnailQuality;
        row.TrickplayQuality = state.TrickplayQuality;
        row.BackgroundWorkerConcurrency = state.BackgroundWorkerConcurrency;
        row.NsfwLanAutoEnable = state.NsfwLanAutoEnable;
        row.HideNsfw = state.HideNsfw;
        row.MetadataStorageDedicated = state.MetadataStorageDedicated;
        row.SubtitlesAutoEnable = state.SubtitlesAutoEnable;
        row.SubtitlesPreferredLanguages = state.SubtitlesPreferredLanguages;
        row.AudioPreferredLanguages = state.AudioPreferredLanguages;
        if (state.SubtitleStyle.TryDecodeAs<SubtitleStyle>(out var subtitleStyle)) {
            row.SubtitleStyle = subtitleStyle;
        }

        row.SubtitleFontScale = state.SubtitleFontScale;
        row.SubtitlePositionPercent = state.SubtitlePositionPercent;
        row.SubtitleOpacity = state.SubtitleOpacity;
        if (state.DefaultPlaybackMode.TryDecodeAs<PlaybackMode>(out var playbackMode)) {
            row.DefaultPlaybackMode = playbackMode;
        }

        row.ShowCastControls = state.ShowCastControls;
        row.HlsTranscoderProfile = HlsTranscoderProfiles
            .ParseOrDefault(state.HlsTranscoderProfile, HlsTranscoderProfile.Software)
            .ToString();
        row.HlsFfmpegPath = string.IsNullOrWhiteSpace(state.HlsFfmpegPath) ? "ffmpeg" : state.HlsFfmpegPath.Trim();
        row.HlsVaapiDevice = string.IsNullOrWhiteSpace(state.HlsVaapiDevice) ? "/dev/dri/renderD128" : state.HlsVaapiDevice.Trim();
    }

    private static LibrarySettings ToContract(LibrarySettingsRow row) =>
        new(
            row.Id,
            row.AutoScanEnabled,
            row.ScanIntervalMinutes,
            row.AutoGenerateMetadata,
            row.AutoGenerateFingerprints,
            row.GeneratePhash,
            row.AutoGeneratePreview,
            row.GenerateTrickplay,
            row.TrickplayIntervalSeconds,
            row.PreviewClipDurationSeconds,
            row.ThumbnailQuality,
            row.TrickplayQuality,
            row.BackgroundWorkerConcurrency,
            row.NsfwLanAutoEnable,
            row.MetadataStorageDedicated,
            row.SubtitlesAutoEnable,
            row.SubtitlesPreferredLanguages,
            row.AudioPreferredLanguages,
            row.SubtitleStyle.ToCode(),
            row.SubtitleFontScale,
            row.SubtitlePositionPercent,
            row.SubtitleOpacity,
            row.DefaultPlaybackMode.ToCode(),
            row.ShowCastControls,
            row.HlsTranscoderProfile,
            row.HlsFfmpegPath,
            row.HlsVaapiDevice,
            row.HideNsfw,
            row.CreatedAt,
            row.UpdatedAt);

    private static LibraryRoot ToContract(LibraryRootRow row) =>
        new(
            row.Id,
            row.Path,
            row.Label,
            row.Enabled,
            row.Recursive,
            row.ScanVideos,
            row.ScanImages,
            row.ScanAudio,
            row.ScanBooks,
            row.IsNsfw,
            row.LastScannedAt,
            row.CreatedAt,
            row.UpdatedAt);
}
