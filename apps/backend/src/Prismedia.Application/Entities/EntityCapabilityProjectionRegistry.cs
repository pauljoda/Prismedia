using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Domain.Taxonomy;
using ContractCapability = Prismedia.Contracts.Entities.EntityCapability;
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

/// <summary>
/// Typed registration point for projecting a hydrated domain entity into its API capabilities.
/// The registry owns applicability and mapping while <see cref="EntityCardProjector"/> only
/// coordinates the registered projections. Adding a contract capability requires one typed
/// registration here; the coverage test rejects unregistered contract capability types.
/// </summary>
internal static class EntityCapabilityProjectionRegistry {
    private static readonly IProjection[] Registrations =
    [
        Register<RatingCapability>(context => new RatingCapability(context.Entity.RatingValue)),
        Register<FlagsCapability>(context => new FlagsCapability(
            context.Entity.IsFavorite,
            context.Entity.IsNsfw,
            context.Entity.IsOrganized,
            context.Entity.IsWanted)),
        RegisterWhen<FileManagementCapability>(
            context => EntityKindRegistry.Describe(context.Entity.Kind).SupportsFileDeletion &&
                       context.FileManagementState.CanDeleteFiles,
            _ => new FileManagementCapability(CanDeleteFiles: true)),

        RegisterSource<EntityProviderIdentity, ProviderIdentityCapability>(
            context => context.Entity.ProviderIdentity,
            (identity, _) => new ProviderIdentityCapability(
                identity.PluginId,
                identity.Identity.Namespace,
                identity.Identity.Value,
                identity.Url)),
        RegisterSource<CapabilityDescription, DescriptionCapability>(
            context => context.Entity.Description,
            (description, _) => new DescriptionCapability(description.Value)),
        RegisterSource<CapabilityPlayback.State, PlaybackCapability>(
            context => context.Entity.Playback,
            (playback, _) => new PlaybackCapability(
                playback.PlayCount,
                playback.SkipCount,
                playback.PlayDuration.TotalSeconds,
                playback.ResumeTime.TotalSeconds,
                playback.LastPlayedAt,
                playback.CompletedAt)),
        RegisterSource<CapabilityMarkers, MarkersCapability>(
            context => context.Entity.MarkerCapability,
            (markers, _) => new MarkersCapability(markers.Items
                .Select(marker => new ContractEntityMarker(
                    marker.Id,
                    marker.Title,
                    marker.Seconds,
                    marker.EndSeconds))
                .ToArray())),
        RegisterSource<CapabilityTechnical, TechnicalCapability>(
            context => context.Entity.Technical,
            (technical, _) => new TechnicalCapability(
                technical.Duration,
                technical.Width,
                technical.Height,
                technical.FrameRate,
                technical.BitRate,
                technical.SampleRate,
                technical.Channels,
                technical.Codec,
                technical.Container,
                technical.Format)),
        Register<ImagesCapability>(context => EntityArtworkProjection.Project(context.Entity)),
        RegisterWhen<FilesCapability>(
            context => context.Entity.EntityFiles.Count > 0,
            context => new FilesCapability(context.Entity.EntityFiles
                .Select(file => new ContractEntityFile(file.Role.ToCode(), file.Path, file.MimeType))
                .ToArray())),
        RegisterWhen<LinksCapability>(
            context => context.Entity.Urls.Count > 0 || context.Entity.ExternalIds.Count > 0,
            context => new LinksCapability(
                context.Entity.Urls
                    .Select(url => new ContractEntityUrl(url.Value, url.Label))
                    .ToArray(),
                context.Entity.ExternalIds
                    .Select(externalId => new ContractEntityExternalId(
                        externalId.Provider,
                        externalId.Value,
                        externalId.Url))
                    .ToArray())),
        RegisterSource<CapabilitySubtitles, SubtitlesCapability>(
            context => context.Entity.SubtitleCapability,
            (subtitles, _) => new SubtitlesCapability(
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
                subtitles.ExtractedAt)),
        RegisterSource<IReadOnlyList<EntityCreditMetadata>, CreditsCapability>(
            SelectCredits,
            (credits, _) => new CreditsCapability(credits)),
        RegisterSource<CapabilityFingerprints, FingerprintsCapability>(
            context => context.Entity.GetCapability<CapabilityFingerprints>(),
            (fingerprints, _) => new FingerprintsCapability(fingerprints.Items
                .Select(fingerprint => new ContractEntityFingerprint(
                    fingerprint.Algorithm.ToCode(),
                    fingerprint.Value))
                .ToArray())),
        RegisterSource<CapabilityStats, StatsCapability>(
            context => context.Entity.Stats,
            (stats, _) => new StatsCapability(stats.Items
                .Select(stat => new ContractEntityStat(stat.Code, stat.Value))
                .ToArray())),
        RegisterSource<CapabilityDates, DatesCapability>(
            context => context.Entity.Dates,
            (dates, _) => new DatesCapability(dates.Items
                .Select(ToContractDate)
                .ToArray())),
        RegisterSource<CapabilityLifetime, LifetimeCapability>(
            context => context.Entity.Lifetime,
            (lifetime, _) => new LifetimeCapability(
                lifetime.Start is null ? null : ToContractDate(lifetime.Start),
                lifetime.End is null ? null : ToContractDate(lifetime.End),
                lifetime.Label)),
        RegisterSource<CapabilitySource, SourceCapability>(
            context => context.Entity.Source,
            (source, _) => new SourceCapability(source.Items
                .Select(item => new ContractEntitySource(item.Code, item.Value))
                .ToArray())),
        RegisterSource<CapabilityProgress, ProgressCapability>(
            context => context.Entity.Progress,
            (progress, _) => new ProgressCapability(
                progress.CurrentEntityId,
                progress.Unit,
                progress.Index,
                progress.Total,
                progress.Mode,
                progress.CompletedAt,
                progress.UpdatedAt,
                Location: progress.Location)),
        RegisterSource<CapabilityPosition, PositionCapability>(
            context => context.Entity.Position,
            (position, _) => new PositionCapability(position.Items
                .Select(item => new ContractEntityPosition(item.Code, item.Value, item.Label))
                .ToArray())),
        RegisterSource<CapabilityClassification, ClassificationCapability>(
            context => context.Entity.Classification,
            (classification, _) => new ClassificationCapability(classification.Value, classification.System)),

        RegisterEntity<Book, BookMetadataCapability>(
            (book, _) => new BookMetadataCapability(book.BookType, book.Format)),
        RegisterEntity<Book, CoverSelectionCapability>(
            (book, _) => new CoverSelectionCapability(book.CoverPageId)),
        RegisterEntity<BookChapter, CoverSelectionCapability>(
            (chapter, _) => new CoverSelectionCapability(chapter.CoverPageId)),
        RegisterEntity<Gallery, GalleryMetadataCapability>(
            (gallery, _) => new GalleryMetadataCapability(gallery.GalleryType)),
        RegisterEntity<Gallery, CoverSelectionCapability>(
            (gallery, _) => new CoverSelectionCapability(gallery.CoverImageId)),
        RegisterEntity<Person, PersonProfileCapability>(
            (person, _) => new PersonProfileCapability(
                person.Disambiguation,
                person.Gender,
                person.Country,
                person.Ethnicity,
                person.EyeColor,
                person.HairColor,
                person.Height,
                person.Weight,
                person.Measurements,
                person.Tattoos,
                person.Piercings)),
        RegisterEntity<AudioTrack, EmbeddedAudioMetadataCapability>(
            (track, _) => new EmbeddedAudioMetadataCapability(track.EmbeddedArtist, track.EmbeddedAlbum)),
        RegisterEntity<Tag, TagPolicyCapability>(
            (tag, _) => new TagPolicyCapability(tag.IgnoreAutoTag)),
        RegisterEntity<Collection, CollectionConfigurationCapability>(
            (collection, context) => new CollectionConfigurationCapability(
                collection.IsShared,
                context.CurrentUserId is { } userId && collection.IsOwnedBy(userId),
                collection.Mode,
                collection.RuleTreeJson,
                collection.CoverMode,
                collection.LastRefreshedAt)),
        RegisterEntity<Collection, CoverSelectionCapability>(
            (collection, _) => new CoverSelectionCapability(collection.CoverItemId)),
        RegisterEntity<VideoSeries, SeriesMetadataCapability>(
            (series, _) => new SeriesMetadataCapability(series.Status))
    ];

    /// <summary>Contract capability types covered by the registered projections.</summary>
    internal static IReadOnlyList<Type> RegisteredCapabilityTypes { get; } =
        Registrations.Select(registration => registration.CapabilityType).ToArray();

    /// <summary>Runs every registered projection in deterministic registration order.</summary>
    internal static IReadOnlyList<ContractCapability> Project(
        Entity entity,
        EntityFileManagementState fileManagementState,
        Guid? currentUserId,
        IReadOnlyList<EntityCreditMetadata>? projectedCreditMetadata) {
        var context = new ProjectionContext(
            entity,
            fileManagementState,
            currentUserId,
            projectedCreditMetadata);
        var capabilities = Registrations
            .Select(registration => registration.Project(context))
            .OfType<ContractCapability>()
            .ToArray();
        var duplicate = capabilities
            .GroupBy(capability => capability.GetType())
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Entity '{entity.Id}' projected capability '{duplicate.Key.Name}' more than once.");
        }

        return capabilities;
    }

    private static IReadOnlyList<EntityCreditMetadata>? SelectCredits(ProjectionContext context) {
        var credits = context.ProjectedCreditMetadata ?? ProjectCredits(context.Entity);
        return context.Entity.Credits is not null || credits.Count > 0 ? credits : null;
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

    private static ContractEntityDate ToContractDate(DomainEntityDate date) =>
        new(date.Code, date.Value, date.SortableValue, date.Precision);

    private static IProjection Register<TCapability>(Func<ProjectionContext, TCapability?> project)
        where TCapability : ContractCapability =>
        new Projection<TCapability>(project);

    private static IProjection RegisterWhen<TCapability>(
        Func<ProjectionContext, bool> applies,
        Func<ProjectionContext, TCapability> project)
        where TCapability : ContractCapability =>
        Register<TCapability>(context => applies(context) ? project(context) : null);

    private static IProjection RegisterSource<TSource, TCapability>(
        Func<ProjectionContext, TSource?> select,
        Func<TSource, ProjectionContext, TCapability> project)
        where TSource : class
        where TCapability : ContractCapability =>
        Register<TCapability>(context =>
            select(context) is { } source ? project(source, context) : null);

    private static IProjection RegisterEntity<TEntity, TCapability>(
        Func<TEntity, ProjectionContext, TCapability> project)
        where TEntity : Entity
        where TCapability : ContractCapability =>
        Register<TCapability>(context =>
            context.Entity is TEntity entity ? project(entity, context) : null);

    private sealed record ProjectionContext(
        Entity Entity,
        EntityFileManagementState FileManagementState,
        Guid? CurrentUserId,
        IReadOnlyList<EntityCreditMetadata>? ProjectedCreditMetadata);

    private interface IProjection {
        Type CapabilityType { get; }

        ContractCapability? Project(ProjectionContext context);
    }

    private sealed class Projection<TCapability>(Func<ProjectionContext, TCapability?> project) : IProjection
        where TCapability : ContractCapability {
        public Type CapabilityType => typeof(TCapability);

        public ContractCapability? Project(ProjectionContext context) => project(context);
    }
}
