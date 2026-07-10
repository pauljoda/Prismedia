using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
using DomainEntityDate = Prismedia.Domain.Capabilities.EntityDate;
using ContractEntityDate = Prismedia.Contracts.Entities.EntityDate;
using ContractEntityExternalId = Prismedia.Contracts.Entities.EntityExternalId;
using ContractEntityFile = Prismedia.Contracts.Entities.EntityFile;
using ContractEntityFingerprint = Prismedia.Contracts.Entities.EntityFingerprint;
using ContractEntityImageAsset = Prismedia.Contracts.Entities.EntityImageAsset;
using ContractEntityMarker = Prismedia.Contracts.Entities.EntityMarker;
using ContractEntityPosition = Prismedia.Contracts.Entities.EntityPosition;
using ContractEntitySource = Prismedia.Contracts.Entities.EntitySource;
using ContractEntityStat = Prismedia.Contracts.Entities.EntityStat;
using ContractEntitySubtitle = Prismedia.Contracts.Entities.EntitySubtitle;
using ContractEntityUrl = Prismedia.Contracts.Entities.EntityUrl;

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

    /// <summary>
    /// Projects an entity using canonical source-ownership truth supplied by its read boundary. Requiring
    /// that fact prevents shallow incidental hydration from silently dropping descendant file management.
    /// </summary>
    public static EntityCard ToCard(Entity entity, bool hasSourceBackedSubtree) =>
        ToCard(entity, new EntityFileManagementState(hasSourceBackedSubtree, HasRecoverableDeletion: false));

    /// <summary>
    /// Projects an Entity using canonical managed-file state supplied by its read boundary. Recoverable
    /// deletion state keeps the action available after source rows are gone without reporting those rows
    /// as source media or enabling deletion for an ordinary fileless Wanted Entity.
    /// </summary>
    public static EntityCard ToCard(Entity entity, EntityFileManagementState fileManagementState) =>
        new() {
            Id = entity.Id,
            Kind = entity.Kind,
            Title = entity.Title,
            ParentEntityId = entity.ParentEntityId,
            SortOrder = entity.SortOrder,
            HasSourceMedia = fileManagementState.HasSourceBackedSubtree,
            Capabilities = MapCapabilities(entity, fileManagementState),
            ChildrenByKind = ToGroups(entity.ChildrenByKind),
            Relationships = ToGroups(entity.RelationshipsByKind),
        };

    /// <summary>Projects the credited people exposed on detail routes.</summary>
    public static IReadOnlyList<EntityCreditMetadata> CreditMetadata(Entity entity) =>
        entity.Credits?.Credits
            .Select(credit => new EntityCreditMetadata(
                credit.Person.Id,
                credit.Role.ToCode(),
                credit.Label,
                [credit.Role.ToCode()],
                credit.Label is null ? [] : [credit.Label]))
            .ToArray() ?? [];

    private static IReadOnlyList<ContractCapability> MapCapabilities(
        Entity entity,
        EntityFileManagementState fileManagementState) {
        var capabilities = new List<ContractCapability>();

        capabilities.Add(new RatingCapability(entity.RatingValue));
        capabilities.Add(new FlagsCapability(entity.IsFavorite, entity.IsNsfw, entity.IsOrganized, entity.IsWanted));

        if (EntityKindRegistry.Describe(entity.Kind).SupportsFileDeletion && fileManagementState.CanDeleteFiles) {
            capabilities.Add(new FileManagementCapability(CanDeleteFiles: true));
        }

        if (entity.ProviderIdentity is { } providerIdentity) {
            capabilities.Add(new ProviderIdentityCapability(
                providerIdentity.PluginId,
                providerIdentity.Identity.Namespace,
                providerIdentity.Identity.Value,
                providerIdentity.Url));
        }

        if (entity.Description is { } description) {
            capabilities.Add(new DescriptionCapability(description.Value));
        }

        if (entity.Playback is { } playback) {
            capabilities.Add(new PlaybackCapability(
                playback.PlayCount,
                playback.SkipCount,
                playback.PlayDuration.TotalSeconds,
                playback.ResumeTime.TotalSeconds,
                playback.LastPlayedAt,
                playback.CompletedAt));
        }

        if (entity.MarkerCapability is { } markers) {
            capabilities.Add(new MarkersCapability(markers.Items
                .Select(marker => new ContractEntityMarker(
                    marker.Id,
                    marker.Title,
                    marker.Seconds,
                    marker.EndSeconds))
                .ToArray()));
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
            capabilities.Add(new FilesCapability(entity.EntityFiles
                .Select(file => new ContractEntityFile(file.Role.ToCode(), file.Path, file.MimeType))
                .ToArray()));
        }

        if (entity.Urls.Count > 0 || entity.ExternalIds.Count > 0) {
            capabilities.Add(new LinksCapability(
                entity.Urls
                    .Select(url => new ContractEntityUrl(url.Value, url.Label))
                    .ToArray(),
                entity.ExternalIds
                    .Select(externalId => new ContractEntityExternalId(
                        externalId.Provider,
                        externalId.Value,
                        externalId.Url))
                    .ToArray()));
        }

        if (entity.SubtitleCapability is { } subtitles) {
            capabilities.Add(new SubtitlesCapability(subtitles.Items
                .Select(subtitle => new ContractEntitySubtitle(
                    subtitle.Id,
                    subtitle.Language,
                    subtitle.Label,
                    subtitle.Format,
                    subtitle.Source,
                    subtitle.StoragePath,
                    subtitle.SourceFormat,
                    subtitle.SourcePath,
                    subtitle.IsDefault))
                .ToArray()));
        }

        if (entity.GetCapability<CapabilityFingerprints>() is { } fingerprints) {
            capabilities.Add(new FingerprintsCapability(fingerprints.Items
                .Select(fingerprint => new ContractEntityFingerprint(
                    fingerprint.Algorithm.ToCode(),
                    fingerprint.Value))
                .ToArray()));
        }

        if (entity.Stats is { } stats) {
            capabilities.Add(new StatsCapability(stats.Items
                .Select(stat => new ContractEntityStat(stat.Code, stat.Value))
                .ToArray()));
        }

        if (entity.Dates is { } dates) {
            capabilities.Add(new DatesCapability(dates.Items
                .Select(ToContractDate)
                .ToArray()));
        }

        if (entity.Lifetime is { } lifetime) {
            capabilities.Add(new LifetimeCapability(
                lifetime.Start is null ? null : ToContractDate(lifetime.Start),
                lifetime.End is null ? null : ToContractDate(lifetime.End),
                lifetime.Label));
        }

        if (entity.Source is { } source) {
            capabilities.Add(new SourceCapability(source.Items
                .Select(item => new ContractEntitySource(item.Code, item.Value))
                .ToArray()));
        }

        if (entity.Progress is { } progress) {
            capabilities.Add(new ProgressCapability(
                progress.CurrentEntityId,
                progress.Unit,
                progress.Index,
                progress.Total,
                progress.Mode,
                progress.CompletedAt,
                progress.UpdatedAt,
                Location: progress.Location));
        }

        if (entity.Position is { } position) {
            capabilities.Add(new PositionCapability(position.Items
                .Select(item => new ContractEntityPosition(item.Code, item.Value, item.Label))
                .ToArray()));
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
            .OrderBy(file => ImageSourcePriority(file.Role, file.Path))
            .ThenBy(file => file.Role switch {
                EntityFileRole.Thumbnail => 0,
                EntityFileRole.Poster => 1,
                EntityFileRole.Cover => 2,
                EntityFileRole.Logo => 3,
                _ => 4
            })
            .Select(file => new ContractEntityImageAsset(file.Role.ToCode(), file.Path, file.MimeType))
            .ToArray();

        var supportedKinds = SupportedManualImageRoles.Select(role => role.ToCode()).ToArray();
        if (assets.Length == 0 && supportedKinds.Length == 0) {
            return null;
        }

        // Cards and rows get the small grid variants when they exist; the full-resolution
        // asset stays on CoverUrl for detail surfaces and the lightbox.
        var gridThumb = entity.EntityFiles
            .FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail)?.Path;
        var gridThumb2x = entity.EntityFiles
            .FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail2x)?.Path;
        return new ImagesCapability(
            supportedKinds,
            assets,
            gridThumb ?? assets.FirstOrDefault()?.Path,
            gridThumb2x,
            assets.FirstOrDefault()?.Path);
    }

    private static IReadOnlyList<EntityGroup> ToGroups(
        IReadOnlyDictionary<EntityKind, IReadOnlyList<Entity>> map) =>
        map.Select(pair => new EntityGroup(
                pair.Key,
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
            .OrderBy(file => ImageSourcePriority(file.Role, file.Path))
            .ThenBy(file => file.Role switch {
                EntityFileRole.Thumbnail => 0,
                EntityFileRole.Poster => 1,
                EntityFileRole.Cover => 2,
                EntityFileRole.Logo => 3,
                _ => 4
            })
            .Select(file => file.Path)
            .FirstOrDefault();

        var gridThumb = entity.EntityFiles
            .FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail)?.Path;
        var coverThumb = gridThumb ?? cover;
        var coverThumb2x = entity.EntityFiles
            .FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail2x)?.Path ?? coverThumb;

        return new EntityThumbnail(
            entity.Id,
            entity.Kind,
            entity.Title,
            entity.ParentEntityId,
            entity.SortOrder,
            cover,
            coverThumb,
            ThumbnailHoverKind.None,
            null,
            [],
            [],
            entity.RatingValue,
            entity.IsFavorite ?? false,
            entity.IsNsfw ?? false,
            entity.IsOrganized ?? false) {
            CoverThumb2xUrl = coverThumb2x,
            IsWanted = entity.IsWanted ?? false,
            Progress = ResolveThumbnailProgress(entity)
        };
    }

    /// <summary>
    /// Computes the 0..1 progress meter fraction for a nested thumbnail from the hydrated
    /// entity's playback and reading-progress capabilities, mirroring the row-based browse
    /// projection so detail-page child grids match library grids.
    /// </summary>
    private static double? ResolveThumbnailProgress(Entity entity) {
        if (entity.Playback is { } playback) {
            if (playback.CompletedAt is not null) {
                return 1.0;
            }

            var duration = entity.Technical?.Duration;
            if (playback.ResumeTime > TimeSpan.Zero && duration is { } total && total > TimeSpan.Zero) {
                return Math.Clamp(playback.ResumeTime.TotalSeconds / total.TotalSeconds, 0, 1);
            }

            return null;
        }

        if (entity.Progress is { } progress) {
            if (progress.CompletedAt is not null) {
                return 1.0;
            }

            if (progress.Total > 0 && progress.Index > 0) {
                return Math.Clamp((double)progress.Index / progress.Total, 0, 1);
            }
        }

        return null;
    }

    private static bool IsCustomPath(string path) =>
        path.Contains("/custom/artwork/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/plugins/artwork/", StringComparison.OrdinalIgnoreCase);

    private static int ImageSourcePriority(EntityFileRole role, string path) =>
        role == EntityFileRole.Backdrop ? 2 : IsCustomPath(path) ? 0 : 1;

    private static ContractEntityDate ToContractDate(DomainEntityDate date) =>
        new(date.Code, date.Value, date.SortableValue, date.Precision);
}
