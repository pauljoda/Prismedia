using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
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
        if (usesVideoTechnical) {
            Add(meta, EntityThumbnailMetaIcons.Video,
                MediaResolutionPolicy.Classify(technical.Width, technical.Height)?.ToCode());
        } else if (technical.Width is { } width && technical.Height is { } height) {
            Add(
                meta,
                EntityThumbnailMetaIcons.Image,
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
        // Preserve exact dimensions for small images rather than treating them as SD video.
        var tier = MediaResolutionPolicy.Classify(width, height);
        if (tier is not null and not MediaResolutionTier.Sd) return tier.Value.ToCode();
        return $"{width}x{height}";
    }
}
