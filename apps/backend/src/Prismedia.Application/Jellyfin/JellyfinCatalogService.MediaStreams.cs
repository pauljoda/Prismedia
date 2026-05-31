using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.Series;
using Prismedia.Contracts.Videos;

namespace Prismedia.Application.Jellyfin;

/// <summary>
/// Media-source and stream DTO mapping for <see cref="JellyfinCatalogService"/>.
/// </summary>
public sealed partial class JellyfinCatalogService {
    private static JellyfinCatalogMediaSourceDto CatalogMediaSource(
        Guid id,
        string name,
        string path,
        string? container,
        string? filePath,
        long? runtimeTicks,
        IReadOnlyList<JellyfinCatalogMediaStreamDto> streams,
        TechnicalCapability? technical = null) {
        long? size = null;
        if (!string.IsNullOrWhiteSpace(filePath)) {
            var file = new FileInfo(filePath);
            size = file.Exists ? file.Length : null;
        }

        var audioIndex = streams
            .Where(stream => stream.Type.Equals(JellyfinProtocol.MediaTypes.Audio, StringComparison.OrdinalIgnoreCase))
            .Select(stream => (int?)stream.Index)
            .FirstOrDefault();

        return new JellyfinCatalogMediaSourceDto {
            Id = id.ToString("N"),
            Path = path,
            Container = container,
            Size = size,
            Name = Path.GetFileName(path),
            ETag = EtagFor(id, filePath ?? path),
            RunTimeTicks = runtimeTicks,
            Bitrate = technical?.BitRate,
            DefaultAudioStreamIndex = audioIndex,
            MediaStreams = streams
        };
    }

    private static IReadOnlyList<JellyfinCatalogMediaStreamDto> CatalogStreams(
        TechnicalCapability? technical,
        string? container,
        SubtitlesCapability? subtitles) {
        var streams = new List<JellyfinCatalogMediaStreamDto> {
            new JellyfinCatalogMediaStreamDto {
                Index = 0,
                Type = JellyfinProtocol.MediaTypes.Video,
                Codec = technical?.Codec,
                DisplayTitle = StreamDisplayTitle(technical, container),
                Width = technical?.Width,
                Height = technical?.Height,
                AverageFrameRate = technical?.FrameRate,
                RealFrameRate = technical?.FrameRate,
                AspectRatio = AspectRatio(technical?.Width, technical?.Height),
                BitRate = technical?.BitRate
            }
        };

        // Audio stream — emitted when the probe captured any audio detail, so clients can resolve a
        // default audio track. Codec is unknown at this layer (the technical capability only carries
        // the video codec); HDR/Dolby-Vision and per-track audio codec metadata are a deferred pass.
        var nextIndex = 1;
        if (technical?.Channels is not null || technical?.SampleRate is not null) {
            streams.Add(new JellyfinCatalogMediaStreamDto {
                Index = nextIndex++,
                Type = JellyfinProtocol.MediaTypes.Audio,
                Channels = technical?.Channels,
                ChannelLayout = ChannelLayout(technical?.Channels),
                SampleRate = technical?.SampleRate,
                IsDefault = true
            });
        }

        if (subtitles?.Items.Count > 0) {
            streams.AddRange(subtitles.Items.Select(subtitle => new JellyfinCatalogMediaStreamDto {
                Index = nextIndex++,
                Type = JellyfinProtocol.MediaTypes.Subtitle,
                Codec = subtitle.Format,
                Language = EmptyAsNull(subtitle.Language),
                DisplayTitle = EmptyAsNull(subtitle.Label) ?? subtitle.Language,
                IsDefault = subtitle.IsDefault,
                IsForced = false,
                IsExternal = true
            }));
        }

        return streams;
    }

    private static string? ChannelLayout(int? channels) =>
        channels switch {
            1 => "mono",
            2 => "stereo",
            6 => "5.1",
            8 => "7.1",
            _ => null
        };

    private static IReadOnlyList<JellyfinCatalogMediaStreamDto> CatalogStreams(
        EntityThumbnail item,
        string? container) =>
        [
            new JellyfinCatalogMediaStreamDto {
                Index = 0,
                Type = JellyfinProtocol.MediaTypes.Video,
                Codec = item.Meta.FirstOrDefault(meta => meta.Icon.Equals("video", StringComparison.OrdinalIgnoreCase) &&
                    !meta.Label.Contains("p", StringComparison.OrdinalIgnoreCase) &&
                    !meta.Label.Equals(container, StringComparison.OrdinalIgnoreCase))?.Label,
                DisplayTitle = item.Meta.FirstOrDefault(meta => meta.Icon.Equals("video", StringComparison.OrdinalIgnoreCase))?.Label
            }
        ];

    private static string? StreamDisplayTitle(TechnicalCapability? technical, string? container) {
        var parts = new[]
        {
            technical?.Height is { } height ? $"{height}p" : null,
            technical?.Codec,
            container
        };
        var title = string.Join(" - ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return title.Length == 0 ? null : title;
    }

    private static EntityFile? SourceFile(IEntityCard item) =>
        item.Capabilities
            .OfType<FilesCapability>()
            .SelectMany(files => files.Items)
            .FirstOrDefault(file => file.Role.Equals("source", StringComparison.OrdinalIgnoreCase));

    private static string VirtualItemPath(Guid id) => $"/{id:N}";

    private static long? RuntimeTicksFrom(EntityThumbnail item) {
        var label = item.Meta.FirstOrDefault(meta => meta.Icon.Equals("duration", StringComparison.OrdinalIgnoreCase))?.Label;
        if (string.IsNullOrWhiteSpace(label)) {
            return null;
        }

        var parts = label.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3 || parts.Any(part => !int.TryParse(part, out _))) {
            return null;
        }

        var values = parts.Select(int.Parse).ToArray();
        var duration = values.Length == 2
            ? new TimeSpan(0, values[0], values[1])
            : new TimeSpan(values[0], values[1], values[2]);
        return duration.Ticks;
    }

    private static string? ContainerFrom(EntityThumbnail item) =>
        item.Meta
            .Where(meta => meta.Icon.Equals("video", StringComparison.OrdinalIgnoreCase))
            .Select(meta => meta.Label)
            .LastOrDefault(label => !label.Contains("p", StringComparison.OrdinalIgnoreCase));

    private static string? ContainerFromPath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.').ToLowerInvariant();
    }

    private sealed record ItemContext(
        Guid? SeriesId,
        string? SeriesName,
        Guid? SeasonId,
        string? SeasonName,
        int? ParentIndexNumber,
        Guid? ParentId = null,
        string? SeriesPrimaryImageTag = null,
        Guid? ParentLogoItemId = null,
        string? ParentLogoImageTag = null,
        Guid? ParentBackdropItemId = null,
        IReadOnlyList<string>? ParentBackdropImageTags = null,
        Guid? ParentThumbItemId = null,
        string? ParentThumbImageTag = null) {
        public static ItemContext? From(JellyfinBaseItemDto item) =>
            item.SeriesId is null &&
            item.SeriesName is null &&
            item.SeasonId is null &&
            item.SeasonName is null &&
            item.ParentIndexNumber is null &&
            item.ParentId is null
                ? null
                : new ItemContext(
                    item.SeriesId,
                    item.SeriesName,
                    item.SeasonId,
                    item.SeasonName,
                    item.ParentIndexNumber,
                    item.ParentId,
                    item.SeriesPrimaryImageTag,
                    item.ParentLogoItemId,
                    item.ParentLogoImageTag,
                    item.ParentBackdropItemId,
                    item.ParentBackdropImageTags,
                    item.ParentThumbItemId,
                    item.ParentThumbImageTag);
    }

}
