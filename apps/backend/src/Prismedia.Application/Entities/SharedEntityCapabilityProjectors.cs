using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using ContractEntityDate = Prismedia.Contracts.Entities.EntityDate;
using ContractEntityExternalId = Prismedia.Contracts.Entities.EntityExternalId;
using ContractEntityFile = Prismedia.Contracts.Entities.EntityFile;
using ContractEntityFingerprint = Prismedia.Contracts.Entities.EntityFingerprint;
using ContractEntityMarker = Prismedia.Contracts.Entities.EntityMarker;
using ContractEntityPosition = Prismedia.Contracts.Entities.EntityPosition;
using ContractEntitySource = Prismedia.Contracts.Entities.EntitySource;
using ContractEntityStat = Prismedia.Contracts.Entities.EntityStat;
using ContractEntitySubtitle = Prismedia.Contracts.Entities.EntitySubtitle;
using ContractEntityUrl = Prismedia.Contracts.Entities.EntityUrl;
using DomainEntityDate = Prismedia.Domain.Capabilities.EntityDate;

namespace Prismedia.Application.Entities;

[EntityCapabilityProjector(10)]
internal sealed class RatingCapabilityProjector : EntityCapabilityProjector<RatingCapability> {
    public override RatingCapability Project(EntityCapabilityProjectionContext context) =>
        new(context.Entity.RatingValue);
}

[EntityCapabilityProjector(20)]
internal sealed class FlagsCapabilityProjector : EntityCapabilityProjector<FlagsCapability> {
    public override FlagsCapability Project(EntityCapabilityProjectionContext context) =>
        new(
            context.Entity.IsFavorite,
            context.Entity.IsNsfw,
            context.Entity.IsOrganized,
            context.Entity.IsWanted);
}

[EntityCapabilityProjector(30)]
internal sealed class FileManagementCapabilityProjector : EntityCapabilityProjector<FileManagementCapability> {
    public override FileManagementCapability? Project(EntityCapabilityProjectionContext context) =>
        EntityKindRegistry.Describe(context.Entity.Kind).SupportsFileDeletion &&
        context.FileManagementState.CanDeleteFiles
            ? new FileManagementCapability(CanDeleteFiles: true)
            : null;
}

[EntityCapabilityProjector(40)]
internal sealed class ProviderIdentityCapabilityProjector : EntityCapabilityProjector<ProviderIdentityCapability> {
    public override ProviderIdentityCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.ProviderIdentity is { } identity
            ? new ProviderIdentityCapability(
                identity.PluginId,
                identity.Identity.Namespace,
                identity.Identity.Value,
                identity.Url)
            : null;
}

[EntityCapabilityProjector(50)]
internal sealed class DescriptionCapabilityProjector : EntityCapabilityProjector<DescriptionCapability> {
    public override DescriptionCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Description is { } description
            ? new DescriptionCapability(description.Value)
            : null;
}

[EntityCapabilityProjector(60)]
internal sealed class ConsumptionCapabilityProjector : EntityCapabilityProjector<ConsumptionCapability> {
    public override ConsumptionCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Consumption is { } consumption
            ? new ConsumptionCapability(
                consumption.AccessCount,
                consumption.CompletionCount,
                consumption.SkipCount,
                consumption.ActiveDuration.TotalSeconds,
                consumption.CompletedAt is null ? consumption.ResumeTime.TotalSeconds : 0,
                consumption.LastAccessedAt,
                consumption.LastActiveAt,
                consumption.CompletedAt)
            : null;
}

[EntityCapabilityProjector(65)]
internal sealed class PlayableVideoCapabilityProjector : EntityCapabilityProjector<PlayableVideoCapability> {
    public override PlayableVideoCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Definition is IPlayableVideoKindDefinition &&
        context.Entity.EntityFiles.Any(file => file.Role == EntityFileRole.Source)
            ? new PlayableVideoCapability()
            : null;
}

[EntityCapabilityProjector(66)]
internal sealed class PlayableAudioCapabilityProjector : EntityCapabilityProjector<PlayableAudioCapability> {
    public override PlayableAudioCapability? Project(EntityCapabilityProjectionContext context) {
        if (context.Entity.Definition is not IAudioPlaybackOwnerKindDefinition owner) {
            return null;
        }

        var policy = owner.AudioPlaybackPolicy;
        var hasPlayableItem = context.Entity.Definition is IPlayableAudioKindDefinition &&
            context.Entity.Kind == policy.ItemKind &&
            HasSource(context.Entity);
        if (!hasPlayableItem && context.Entity.ChildrenByKind.TryGetValue(policy.ItemKind, out var children)) {
            hasPlayableItem = children.Any(child =>
                child.Definition is IPlayableAudioKindDefinition && HasSource(child));
        }

        hasPlayableItem = hasPlayableItem || context.SourceBackedChildKinds.Contains(policy.ItemKind);

        return hasPlayableItem
            ? new PlayableAudioCapability(
                policy.ItemKind,
                policy.PreservesQueueOrder,
                policy.SupportsPlaybackRate)
            : null;
    }

    private static bool HasSource(Entity entity) =>
        entity.EntityFiles.Any(file => file.Role == EntityFileRole.Source);
}

[EntityCapabilityProjector(67)]
internal sealed class OrderedSequenceCapabilityProjector : EntityCapabilityProjector<OrderedSequenceCapability> {
    public override OrderedSequenceCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Definition.ProgressTopology switch {
            EntityProgressTopology.OrderedContainerTopology container => new OrderedSequenceCapability(
                EntitySequenceRole.Container,
                container.ItemKind,
                []),
            EntityProgressTopology.OrderedRollupTopology item => new OrderedSequenceCapability(
                EntitySequenceRole.Item,
                item.ItemKind,
                item.ContainerKinds),
            _ => null
        };
}

[EntityCapabilityProjector(68)]
internal sealed class PageSequenceCapabilityProjector : EntityCapabilityProjector<PageSequenceCapability> {
    public override PageSequenceCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.GetCapability<CapabilityPageSequence>() is { } pages
            ? new PageSequenceCapability(
                pages.PageCount,
                pages.Direction,
                pages.DefaultMode,
                pages.CoverOrdinal)
            : null;
}

[EntityCapabilityProjector(70)]
internal sealed class MarkersCapabilityProjector : EntityCapabilityProjector<MarkersCapability> {
    public override MarkersCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.MarkerCapability is { } markers
            ? new MarkersCapability(markers.Items
                .Select(marker => new ContractEntityMarker(
                    marker.Id,
                    marker.Title,
                    marker.Seconds,
                    marker.EndSeconds))
                .ToArray())
            : null;
}

[EntityCapabilityProjector(80)]
internal sealed class TechnicalCapabilityProjector : EntityCapabilityProjector<TechnicalCapability> {
    public override TechnicalCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Technical is { } technical
            ? new TechnicalCapability(
                technical.Duration,
                technical.Width,
                technical.Height,
                technical.FrameRate,
                technical.BitRate,
                technical.SampleRate,
                technical.Channels,
                technical.Codec,
                technical.Container,
                technical.Format)
            : null;
}

[EntityCapabilityProjector(90)]
internal sealed class ImagesCapabilityProjector : EntityCapabilityProjector<ImagesCapability> {
    public override ImagesCapability Project(EntityCapabilityProjectionContext context) =>
        EntityArtworkProjection.Project(context.Entity);
}

[EntityCapabilityProjector(100)]
internal sealed class FilesCapabilityProjector : EntityCapabilityProjector<FilesCapability> {
    public override FilesCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.EntityFiles.Count > 0
            ? new FilesCapability(context.Entity.EntityFiles
                .Select(file => new ContractEntityFile(file.Role, file.Path, file.MimeType))
                .ToArray())
            : null;
}

[EntityCapabilityProjector(110)]
internal sealed class LinksCapabilityProjector : EntityCapabilityProjector<LinksCapability> {
    public override LinksCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Urls.Count > 0 || context.Entity.ExternalIds.Count > 0
            ? new LinksCapability(
                context.Entity.Urls
                    .Select(url => new ContractEntityUrl(url.Value, url.Label))
                    .ToArray(),
                context.Entity.ExternalIds
                    .Select(externalId => new ContractEntityExternalId(
                        externalId.Provider,
                        externalId.Value,
                        externalId.Url))
                    .ToArray())
            : null;
}

[EntityCapabilityProjector(120)]
internal sealed class SubtitlesCapabilityProjector : EntityCapabilityProjector<SubtitlesCapability> {
    public override SubtitlesCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.SubtitleCapability is { } subtitles
            ? new SubtitlesCapability(
                subtitles.Items
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
                    .ToArray(),
                subtitles.ExtractedAt)
            : null;
}

[EntityCapabilityProjector(130)]
internal sealed class CreditsCapabilityProjector : EntityCapabilityProjector<CreditsCapability> {
    public override CreditsCapability? Project(EntityCapabilityProjectionContext context) {
        var credits = context.ProjectedCreditMetadata ?? ProjectCredits(context.Entity);
        return context.Entity.Credits is not null || credits.Count > 0
            ? new CreditsCapability(credits)
            : null;
    }

    private static IReadOnlyList<EntityCreditMetadata> ProjectCredits(Entity entity) =>
        entity.Credits?.Credits
            .Select(credit => new EntityCreditMetadata(
                credit.Person.Id,
                credit.Role.ToCode(),
                credit.Label,
                [credit.Role.ToCode()],
                credit.Label is null ? [] : [credit.Label]))
            .ToArray() ?? [];
}

[EntityCapabilityProjector(140)]
internal sealed class FingerprintsCapabilityProjector : EntityCapabilityProjector<FingerprintsCapability> {
    public override FingerprintsCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.GetCapability<CapabilityFingerprints>() is { } fingerprints
            ? new FingerprintsCapability(fingerprints.Items
                .Select(fingerprint => new ContractEntityFingerprint(
                    fingerprint.Algorithm.ToCode(),
                    fingerprint.Value))
                .ToArray())
            : null;
}

[EntityCapabilityProjector(150)]
internal sealed class StatsCapabilityProjector : EntityCapabilityProjector<StatsCapability> {
    public override StatsCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Stats is { } stats
            ? new StatsCapability(stats.Items
                .Select(stat => new ContractEntityStat(stat.Code, stat.Value))
                .ToArray())
            : null;
}

[EntityCapabilityProjector(160)]
internal sealed class DatesCapabilityProjector : EntityCapabilityProjector<DatesCapability> {
    public override DatesCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Dates is { } dates
            ? new DatesCapability(dates.Items
                .Select(EntityCapabilityProjectionConversions.ToContractDate)
                .ToArray())
            : null;
}

[EntityCapabilityProjector(170)]
internal sealed class LifetimeCapabilityProjector : EntityCapabilityProjector<LifetimeCapability> {
    public override LifetimeCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Lifetime is { } lifetime
            ? new LifetimeCapability(
                lifetime.Start is null
                    ? null
                    : EntityCapabilityProjectionConversions.ToContractDate(lifetime.Start),
                lifetime.End is null
                    ? null
                    : EntityCapabilityProjectionConversions.ToContractDate(lifetime.End),
                lifetime.Label)
            : null;
}

[EntityCapabilityProjector(180)]
internal sealed class SourceCapabilityProjector : EntityCapabilityProjector<SourceCapability> {
    public override SourceCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Source is { } source
            ? new SourceCapability(source.Items
                .Select(item => new ContractEntitySource(item.Code, item.Value))
                .ToArray())
            : null;
}

[EntityCapabilityProjector(190)]
internal sealed class ProgressCapabilityProjector : EntityCapabilityProjector<ProgressCapability> {
    public override ProgressCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Progress is { } progress
            ? new ProgressCapability(
                progress.CurrentEntityId,
                progress.Unit,
                progress.Index,
                progress.Total,
                progress.Mode,
                progress.CompletedAt,
                progress.UpdatedAt,
                Location: progress.Location,
                ConsumedCount: progress.ConsumedCount,
                ConsumedTotal: progress.Total,
                ConsumedPercent: progress.Total > 0
                    ? Math.Clamp(progress.ConsumedCount / (double)progress.Total, 0, 1)
                    : 0)
            : null;
}

[EntityCapabilityProjector(200)]
internal sealed class PositionCapabilityProjector : EntityCapabilityProjector<PositionCapability> {
    public override PositionCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Position is { } position
            ? new PositionCapability(position.Items
                .Select(item => new ContractEntityPosition(item.Code, item.Value, item.Label))
                .ToArray())
            : null;
}

[EntityCapabilityProjector(210)]
internal sealed class ClassificationCapabilityProjector : EntityCapabilityProjector<ClassificationCapability> {
    public override ClassificationCapability? Project(EntityCapabilityProjectionContext context) =>
        context.Entity.Classification is { } classification
            ? new ClassificationCapability(classification.Value, classification.System)
            : null;
}

internal static class EntityCapabilityProjectionConversions {
    internal static ContractEntityDate ToContractDate(DomainEntityDate date) =>
        new(date.Code, date.Value, date.SortableValue, date.Precision);
}
