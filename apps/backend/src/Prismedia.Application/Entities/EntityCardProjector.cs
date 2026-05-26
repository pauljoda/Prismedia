using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;

namespace Prismedia.Application.Entities;

/// <summary>
/// The single projection from a hydrated domain <see cref="Entity"/> to the API
/// <see cref="EntityCard"/> contract. This is the only place domain capabilities are
/// mapped to contract capabilities; the row-based browse/thumbnail path in
/// Infrastructure is the one deliberate read-optimized exception and does not flow
/// through here.
/// </summary>
public static class EntityCardProjector {
    private static readonly EntityFileRole[] SupportedManualImageRoles =
    [
        EntityFileRole.Thumbnail,
        EntityFileRole.Poster,
        EntityFileRole.Backdrop,
        EntityFileRole.Cover,
        EntityFileRole.Logo
    ];

    /// <summary>Projects a hydrated domain entity to the shared entity card contract.</summary>
    public static EntityCard ToCard(Entity entity) =>
        new() {
            Id = entity.Id,
            Kind = EntityKindRegistry.ToCode(entity.Kind),
            Title = entity.Title,
            ParentEntityId = entity.ParentEntityId,
            SortOrder = entity.SortOrder,
            Capabilities = MapCapabilities(entity),
            ChildrenByKind = ToGroups(entity.ChildrenByKind),
            Relationships = ToGroups(entity.RelationshipsByKind),
        };

    /// <summary>Projects the credited people exposed on detail routes.</summary>
    public static IReadOnlyList<EntityCreditMetadata> CreditMetadata(Entity entity) =>
        entity.Credits?.Credits
            .Select(credit => new EntityCreditMetadata(
                credit.Person.Id,
                credit.Role.ToCode(),
                credit.Label))
            .ToArray() ?? [];

    private static IReadOnlyList<ContractCapability> MapCapabilities(Entity entity) {
        var capabilities = new List<ContractCapability>();

        capabilities.Add(new RatingCapability(entity.RatingValue));
        capabilities.Add(new FlagsCapability(entity.IsFavorite, entity.IsNsfw, entity.IsOrganized));

        if (entity.Description is { } description) {
            capabilities.Add(new DescriptionCapability(description.Value));
        }

        if (entity.Playback is { } playback) {
            capabilities.Add(new PlaybackCapability(
                playback.PlayCount,
                playback.PlayDuration.TotalSeconds,
                playback.ResumeTime.TotalSeconds,
                playback.LastPlayedAt,
                playback.CompletedAt));
        }

        if (entity.MarkerCapability is { } markers) {
            capabilities.Add(new MarkersCapability(markers.Items));
        }

        if (entity.Technical is { } technical) {
            capabilities.Add(new TechnicalCapability(
                technical.Duration,
                technical.Width,
                technical.Height,
                technical.FrameRate,
                technical.BitRate,
                technical.SampleRate,
                technical.Channels,
                technical.Codec,
                technical.Container,
                technical.Format));
        }

        var images = ProjectImages(entity);
        if (images is not null) {
            capabilities.Add(images);
        }

        if (entity.EntityFiles.Count > 0) {
            capabilities.Add(new FilesCapability(entity.EntityFiles));
        }

        if (entity.Urls.Count > 0 || entity.ExternalIds.Count > 0) {
            capabilities.Add(new LinksCapability(entity.Urls, entity.ExternalIds));
        }

        if (entity.SubtitleCapability is { } subtitles) {
            capabilities.Add(new SubtitlesCapability(subtitles.Items));
        }

        if (entity.GetCapability<CapabilityFingerprints>() is { } fingerprints) {
            capabilities.Add(new FingerprintsCapability(fingerprints.Items));
        }

        if (entity.Stats is { } stats) {
            capabilities.Add(new StatsCapability(stats.Items));
        }

        if (entity.Dates is { } dates) {
            capabilities.Add(new DatesCapability(dates.Items));
        }

        if (entity.Lifetime is { } lifetime) {
            capabilities.Add(new LifetimeCapability(lifetime.Start, lifetime.End, lifetime.Label));
        }

        if (entity.Source is { } source) {
            capabilities.Add(new SourceCapability(source.Items));
        }

        if (entity.Progress is { } progress) {
            capabilities.Add(new ProgressCapability(
                progress.CurrentEntityId,
                progress.Unit,
                progress.Index,
                progress.Total,
                progress.Mode,
                progress.CompletedAt,
                progress.UpdatedAt));
        }

        if (entity.Position is { } position) {
            capabilities.Add(new PositionCapability(position.Items));
        }

        if (entity.Classification is { } classification) {
            capabilities.Add(new ClassificationCapability(classification.Value, classification.System));
        }

        return capabilities;
    }

    private static ImagesCapability? ProjectImages(Entity entity) {
        var assets = entity.EntityFiles
            .Where(file => file.Role is EntityFileRole.Thumbnail or EntityFileRole.Poster
                or EntityFileRole.Cover or EntityFileRole.Backdrop or EntityFileRole.Logo)
            .OrderBy(file => file.Role switch {
                _ when IsCustomPath(file.Path) => -1,
                EntityFileRole.Thumbnail => 0,
                EntityFileRole.Poster => 1,
                EntityFileRole.Cover => 2,
                EntityFileRole.Logo => 3,
                _ => 4
            })
            .Select(file => new EntityImageAsset(file.Role, file.Path, file.MimeType))
            .ToArray();

        var supportedKinds = SupportedManualImageRoles.Select(role => role.ToCode()).ToArray();
        if (assets.Length == 0 && supportedKinds.Length == 0) {
            return null;
        }

        return new ImagesCapability(supportedKinds, assets, assets.FirstOrDefault()?.Path, assets.FirstOrDefault()?.Path);
    }

    private static IReadOnlyList<EntityGroup> ToGroups(
        IReadOnlyDictionary<EntityKind, IReadOnlyList<Entity>> map) =>
        map.Select(pair => new EntityGroup(
                EntityKindRegistry.ToCode(pair.Key),
                EntityKindRegistry.Describe(pair.Key).GroupLabel,
                pair.Value
                    .OrderBy(child => child.SortOrder ?? int.MaxValue)
                    .ThenBy(child => child.Title)
                    .Select(ToThumbnail)
                    .ToArray()))
            .ToArray();

    private static EntityThumbnail ToThumbnail(Entity entity) {
        var cover = entity.EntityFiles
            .Where(file => file.Role is EntityFileRole.Thumbnail or EntityFileRole.Poster
                or EntityFileRole.Cover or EntityFileRole.Backdrop or EntityFileRole.Logo)
            .OrderBy(file => file.Role switch {
                _ when IsCustomPath(file.Path) => -1,
                EntityFileRole.Thumbnail => 0,
                EntityFileRole.Poster => 1,
                EntityFileRole.Cover => 2,
                EntityFileRole.Logo => 3,
                _ => 4
            })
            .Select(file => file.Path)
            .FirstOrDefault();

        return new EntityThumbnail(
            entity.Id,
            EntityKindRegistry.ToCode(entity.Kind),
            entity.Title,
            entity.ParentEntityId,
            entity.SortOrder,
            cover,
            "none",
            null,
            [],
            [],
            entity.RatingValue,
            entity.IsFavorite ?? false,
            entity.IsNsfw ?? false,
            entity.IsOrganized ?? false);
    }

    private static bool IsCustomPath(string path) =>
        path.Contains("/custom/artwork/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/plugins/artwork/", StringComparison.OrdinalIgnoreCase);
}
