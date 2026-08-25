using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Compact metadata projection helpers for entity thumbnails.
/// </summary>
public sealed partial class EfEntityReadService {
    private static IReadOnlyList<EntityThumbnailMeta> ProjectThumbnailMeta(
        EntityRow row,
        EntityTechnicalRow? technical,
        AudioTrackDetailRow? audioDetail) {
        var meta = new List<EntityThumbnailMeta>(MaxThumbnailMeta);

        // Album and artist queues are built from compact thumbnails rather than full Entity
        // documents. Preserve an embedded track artist here so every player can prefer it over
        // the structural album artist while retaining that album value as a fallback.
        Add(meta, EntityThumbnailMetaIcons.Person, audioDetail?.EmbeddedArtist);

        if (technical is null) {
            return meta;
        }

        Add(meta, EntityThumbnailMetaIcons.Duration, FormatDuration(technical.DurationSeconds));
        EntityKindRegistry.TryDescribe(row.KindCode, out var definition);
        var usesVideoTechnical = definition is IPlayableVideoKindDefinition;
        if (technical.Width is { } width && technical.Height is { } height) {
            Add(
                meta,
                usesVideoTechnical ? EntityThumbnailMetaIcons.Video : EntityThumbnailMetaIcons.Image,
                FormatResolution(width, height));
        }

        if (usesVideoTechnical) {
            Add(meta, EntityThumbnailMetaIcons.Video, technical.Codec?.ToUpperInvariant());
            Add(meta, EntityThumbnailMetaIcons.Video, technical.Container?.ToUpperInvariant());
        } else if (definition?.MediaQualityFamily == EntityMediaQualityFamily.Audio) {
            Add(meta, EntityThumbnailMetaIcons.Audio, technical.Codec?.ToUpperInvariant());
        }

        return meta.Take(MaxThumbnailMeta).ToArray();
    }

    private static void Add(List<EntityThumbnailMeta> meta, string icon, string? label) {
        if (!string.IsNullOrWhiteSpace(label)) {
            meta.Add(new EntityThumbnailMeta(icon, label));
        }
    }

    private static string? FormatDuration(double? seconds) {
        if (seconds is not { } value || !double.IsFinite(value) || value <= 0) {
            return null;
        }

        var duration = TimeSpan.FromSeconds(Math.Round(value));
        if (duration.TotalHours >= 1) {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatResolution(int width, int height) {
        // Width participates so scope and other cropped masters keep their source tier.
        // For example, a 3840x1920 frame is still a 4K source even though its shorter
        // edge sits below the conventional 2160-line threshold.
        if (width >= 7600 || height >= 4300) return "8K";
        if (width >= 3800 || height >= 2000) return "4K";
        if (width >= 2540 || height >= 1400) return "1440p";
        if (width >= 1800 || height >= 1000) return "1080p";
        if (width >= 1200 || height >= 700) return "720p";
        if (width >= 640 || height >= 480) return "480p";
        return $"{width}x{height}";
    }
}
