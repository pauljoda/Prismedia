using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Requests;

public sealed partial class WantedEntityWriter {
    /// <summary>
    /// Applies reviewed metadata without downloading artwork on the request boundary. Remote image URLs
    /// are persisted as ordinary artwork-role references so entity grids can render the exact images the
    /// review already showed; a later metadata hydration atomically replaces each URL with its cached path.
    /// </summary>
    public async Task ApplyProposalWithDeferredArtworkAsync(
        Guid entityId,
        Contracts.Plugins.EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        await ExecuteIfLifecycleMutableAsync(
            entityId,
            async leaseCancellationToken => {
                await TrackDeferredArtworkReferencesAsync(entityId, proposal, leaseCancellationToken);
                var metadataOnly = WithoutArtwork(proposal);
                var fields = ProposalApplySelection.SelectAllPresentFields(metadataOnly);
                await apply.ApplyAsync(
                    entityId,
                    metadataOnly,
                    fields,
                    selectedImages: null,
                    leaseCancellationToken);
                await TrackMaterializedRelationshipArtworkReferencesAsync(
                    entityId,
                    proposal,
                    leaseCancellationToken);
            },
            cancellationToken);
    }

    private async Task TrackDeferredArtworkReferencesAsync(
        Guid rootEntityId,
        Contracts.Plugins.EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var references = EnumerateTargetedProposals(rootEntityId, proposal)
            .SelectMany(target => (ProposalApplySelection.SelectDefaultImages(target.Proposal)
                    ?? new Dictionary<string, string?>())
                .Where(image => image.Value is not null)
                .Select(image => new DeferredArtworkReference(
                    target.EntityId,
                    ImageKindRoleResolver.RoleFor(image.Key),
                    image.Value!)))
            .ToArray();
        await UpsertDeferredArtworkReferencesAsync(references, cancellationToken);
    }

    /// <summary>
    /// Persists artwork for related entities that the metadata apply just materialized. Resolving through
    /// the owner's persisted relationship links avoids attaching an image to an unrelated same-name entity.
    /// </summary>
    private async Task TrackMaterializedRelationshipArtworkReferencesAsync(
        Guid rootEntityId,
        Contracts.Plugins.EntityMetadataProposal proposal,
        CancellationToken cancellationToken) {
        var owners = EnumerateTargetedProposals(rootEntityId, proposal).ToArray();
        var pending = owners
            .SelectMany(owner => RelationshipProposals(owner.Proposal)
                .Where(relationship => relationship.TargetEntityId is null
                    && relationship.TargetKind.IsRelationship()
                    && !string.IsNullOrWhiteSpace(relationship.Patch.Title)
                    && ProposalApplySelection.SelectDefaultImages(relationship) is { Count: > 0 })
                .Select(relationship => new {
                    OwnerEntityId = owner.EntityId,
                    Proposal = relationship,
                    KindCode = relationship.TargetKind.ToCode(),
                    Title = relationship.Patch.Title!.Trim()
                }))
            .ToArray();
        if (pending.Length == 0) {
            return;
        }

        var ownerIds = pending.Select(item => item.OwnerEntityId).Distinct().ToArray();
        var linkedTargets = await db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => ownerIds.Contains(link.EntityId))
            .Join(
                db.Entities.AsNoTracking(),
                link => link.TargetEntityId,
                entity => entity.Id,
                (link, entity) => new {
                    OwnerEntityId = link.EntityId,
                    EntityId = entity.Id,
                    entity.KindCode,
                    entity.Title
                })
            .ToArrayAsync(cancellationToken);

        var resolved = pending
            .Select(item => new {
                item.Proposal,
                TargetEntityIds = linkedTargets
                    .Where(target => target.OwnerEntityId == item.OwnerEntityId
                        && string.Equals(target.KindCode, item.KindCode, StringComparison.Ordinal)
                        && string.Equals(target.Title, item.Title, StringComparison.OrdinalIgnoreCase))
                    .Select(target => target.EntityId)
                    .Distinct()
                    .ToArray()
            })
            .Where(item => item.TargetEntityIds.Length == 1)
            .ToArray();
        var references = resolved
            .SelectMany(item => ProposalApplySelection.SelectDefaultImages(item.Proposal)!
                .Where(image => image.Value is not null)
                .Select(image => new DeferredArtworkReference(
                    item.TargetEntityIds[0],
                    ImageKindRoleResolver.RoleFor(image.Key),
                    image.Value!)))
            .ToArray();
        if (references.Length == 0) {
            return;
        }

        await UpsertDeferredArtworkReferencesAsync(references, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertDeferredArtworkReferencesAsync(
        IReadOnlyCollection<DeferredArtworkReference> candidates,
        CancellationToken cancellationToken) {
        var references = candidates
            .GroupBy(reference => (reference.EntityId, reference.Role))
            .Select(group => group.First())
            .ToArray();
        if (references.Length == 0) {
            return;
        }

        var entityIds = references.Select(reference => reference.EntityId).Distinct().ToArray();
        var roles = references.Select(reference => reference.Role).Distinct().ToArray();
        var existing = await db.EntityFiles
            .Where(file => entityIds.Contains(file.EntityId) && roles.Contains(file.Role))
            .ToArrayAsync(cancellationToken);
        var filesByRole = existing.ToDictionary(file => (file.EntityId, file.Role));
        var now = DateTimeOffset.UtcNow;

        foreach (var reference in references) {
            if (!filesByRole.TryGetValue((reference.EntityId, reference.Role), out var file)) {
                file = new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = reference.EntityId,
                    Role = reference.Role,
                    Path = reference.Url,
                    Source = FileSourceKind.Custom.ToCode(),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.EntityFiles.Add(file);
                filesByRole[(reference.EntityId, reference.Role)] = file;
                continue;
            }

            // Never replace an already-localized or user-uploaded asset with a remote reference.
            if (Uri.TryCreate(file.Path, UriKind.Absolute, out var current)
                && (string.Equals(current.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))) {
                file.Path = reference.Url;
                file.Source = FileSourceKind.Custom.ToCode();
                file.UpdatedAt = now;
            }
        }
    }

    private static IEnumerable<Contracts.Plugins.EntityMetadataProposal> RelationshipProposals(
        Contracts.Plugins.EntityMetadataProposal proposal) =>
        proposal.Children
            .Where(child => child.TargetKind.IsRelationship())
            .Concat(proposal.Relationships ?? []);

    private static IEnumerable<(Guid EntityId, Contracts.Plugins.EntityMetadataProposal Proposal)>
        EnumerateTargetedProposals(
            Guid rootEntityId,
            Contracts.Plugins.EntityMetadataProposal proposal) {
        yield return (rootEntityId, proposal);
        foreach (var child in proposal.Children.Concat(proposal.Relationships ?? [])) {
            if (child.TargetEntityId is not { } childEntityId) {
                continue;
            }
            foreach (var target in EnumerateTargetedProposals(childEntityId, child)) {
                yield return target;
            }
        }
    }

    private static Contracts.Plugins.EntityMetadataProposal WithoutArtwork(
        Contracts.Plugins.EntityMetadataProposal proposal) =>
        proposal with {
            Images = [],
            Children = proposal.Children.Select(WithoutArtwork).ToArray(),
            Relationships = (proposal.Relationships ?? []).Select(WithoutArtwork).ToArray()
        };

    private sealed record DeferredArtworkReference(Guid EntityId, EntityFileRole Role, string Url);
}
