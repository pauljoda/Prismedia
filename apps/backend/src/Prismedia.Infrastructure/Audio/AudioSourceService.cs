using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Audio;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Audio;

/// <summary>
/// EF-backed implementation that resolves source audio files from the shared file capability table.
/// </summary>
public sealed class AudioSourceService : IAudioSourceService {
    private static readonly HashSet<string> BrowserNativeCodecs = new(StringComparer.OrdinalIgnoreCase) {
        "aac",
        "flac",
        "mp3",
        "opus",
        "pcm_s16le",
        "pcm_s24le",
        "vorbis"
    };

    private readonly PrismediaDbContext _db;

    /// <summary>
    /// Creates an audio source resolver over the database context.
    /// </summary>
    /// <param name="db">Database context used to find audio source file rows.</param>
    public AudioSourceService(PrismediaDbContext db) {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<AudioSourceFile?> GetSourceAsync(Guid id, CancellationToken cancellationToken) {
        var source = await (
            from entity in _db.Entities.AsNoTracking()
            join file in _db.EntityFiles.AsNoTracking() on entity.Id equals file.EntityId
            join technical in _db.EntityTechnical.AsNoTracking() on entity.Id equals technical.EntityId into technicalRows
            from technical in technicalRows.DefaultIfEmpty()
            where entity.Id == id &&
                entity.KindCode == EntityKindRegistry.AudioTrack.Code &&
                file.Role == EntityFileRole.Source
            select new {
                File = file,
                Technical = technical
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (source is null || !File.Exists(source.File.Path)) {
            return null;
        }

        var extension = Path.GetExtension(source.File.Path);

        return new AudioSourceFile(
            id,
            source.File.Path,
            source.File.MimeType ?? MimeForExtension(extension),
            source.Technical?.DurationSeconds,
            source.Technical?.Codec,
            IsDirectPlayable(source.Technical?.Codec));
    }

    private static bool IsDirectPlayable(string? codec) =>
        string.IsNullOrWhiteSpace(codec) || BrowserNativeCodecs.Contains(codec.Trim());

    private static string MimeForExtension(string extension) {
        return extension.ToLowerInvariant() switch {
            ".mp3" => MediaContentTypes.AudioMpeg,
            ".m4a" or ".m4b" or ".aac" => MediaContentTypes.AudioMp4,
            ".ogg" or ".oga" => MediaContentTypes.AudioOgg,
            ".opus" => MediaContentTypes.AudioOpus,
            ".flac" => MediaContentTypes.AudioFlac,
            ".wav" => MediaContentTypes.AudioWav,
            ".aiff" or ".aif" => MediaContentTypes.AudioAiff,
            ".wma" => MediaContentTypes.AudioWma,
            _ => MediaContentTypes.OctetStream
        };
    }
}
