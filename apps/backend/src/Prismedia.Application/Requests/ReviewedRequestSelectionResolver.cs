using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>One fresh proposal node paired with its server-derived persistent identity.</summary>
internal sealed record ResolvedRequestProposalNode(
    EntityMetadataProposal Proposal,
    ExternalIdentity Identity);

/// <summary>The validated root-or-direct-child selection for one reviewed commit.</summary>
internal sealed record ReviewedRequestSelection(
    bool SelectRoot,
    IReadOnlyList<ResolvedRequestProposalNode> Nodes);

/// <summary>
/// Converts untrusted proposal-id selectors into fresh server-owned proposal nodes and identities.
/// Selection depth and per-kind target compatibility stay centralized here rather than leaking into
/// endpoints or wanted/acquisition persistence.
/// </summary>
internal static class ReviewedRequestSelectionResolver {
    /// <summary>Resolves the legal selection for a fresh canonical review.</summary>
    public static ReviewedRequestSelection Resolve(
        RequestKindDescriptor descriptor,
        RequestReviewResponse review,
        IReadOnlyList<string> selectedProposalIds,
        MonitorPreset? preset) {
        var targets = new Dictionary<string, RequestReviewTarget>(StringComparer.Ordinal);
        var requestableIdentities = new HashSet<(EntityKind Kind, ExternalIdentity Identity)>();
        foreach (var target in review.Targets) {
            if (string.IsNullOrWhiteSpace(target.ProposalId) || !targets.TryAdd(target.ProposalId, target)) {
                throw new RequestCommitValidationException("The plugin returned duplicate or empty proposal ids.");
            }
            if (target.Requestable && !requestableIdentities.Add((target.EntityKind, target.ExternalIdentity))) {
                throw new RequestCommitValidationException(
                    "The plugin returned multiple requestable targets with the same kind and external identity.");
            }
        }

        if (!targets.TryGetValue(review.Proposal.ProposalId, out var rootTarget)
            || rootTarget.Kind != descriptor.Kind
            || rootTarget.EntityKind != review.Proposal.TargetKind.ToEntityKind()
            || rootTarget.ExternalIdentity != review.ExternalIdentity
            || !rootTarget.Requestable) {
            throw new RequestCommitValidationException("The reviewed root is not a valid request target.");
        }

        var childDescriptor = RequestKindRegistry.ChildOf(descriptor);
        var direct = review.Proposal.Children
            .Where(node => !node.TargetKind.IsRelationship())
            .ToArray();
        var directIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in direct) {
            if (string.IsNullOrWhiteSpace(node.ProposalId) || !directIds.Add(node.ProposalId)) {
                throw new RequestCommitValidationException("The plugin returned duplicate or empty direct-child proposal ids.");
            }
        }

        if (descriptor.IsContainer) {
            if (childDescriptor is null) {
                throw new RequestCommitValidationException("This container has no requestable child kind.");
            }

            IReadOnlyList<string> selectedIds = selectedProposalIds;
            if (selectedIds.Count == 0) {
                if (preset is null) {
                    throw new RequestCommitValidationException(
                        "Select at least one item to request, or choose a monitoring preset.");
                }

                selectedIds = MonitorPresetSelection.Resolve(
                    preset.Value,
                    direct.Select(node => {
                        var target = TargetFor(node, childDescriptor, targets);
                        return new MonitorPresetCandidate(target.ProposalId, Owned: false);
                    }).ToArray());
            }

            return new ReviewedRequestSelection(
                SelectRoot: false,
                ResolveDirectNodes(direct, childDescriptor, targets, selectedIds));
        }

        // A book proposal can represent a series shell whose direct Book children are the actual sibling
        // volumes. That is a fan-out selection, not structural phantom materialization.
        if (descriptor.ChildKind is not null && !descriptor.MaterializeChildPhantoms && direct.Length > 0) {
            if (childDescriptor is null || selectedProposalIds.Count == 0) {
                throw new RequestCommitValidationException("Select at least one reviewed child proposal.");
            }

            return new ReviewedRequestSelection(
                SelectRoot: false,
                ResolveDirectNodes(direct, childDescriptor, targets, selectedProposalIds));
        }

        if (selectedProposalIds.Count != 1
            || !string.Equals(selectedProposalIds[0], review.Proposal.ProposalId, StringComparison.Ordinal)) {
            throw new RequestCommitValidationException("Select the reviewed root proposal for this request.");
        }

        return new ReviewedRequestSelection(SelectRoot: true, []);
    }

    /// <summary>
    /// Resolves selected direct structural nodes against server-derived targets, preserving proposal order.
    /// </summary>
    public static IReadOnlyList<ResolvedRequestProposalNode> ResolveDirectNodes(
        IReadOnlyList<EntityMetadataProposal> direct,
        RequestKindDescriptor expectedDescriptor,
        IReadOnlyDictionary<string, RequestReviewTarget> targets,
        IReadOnlyList<string> selectedIds) {
        var picked = selectedIds.ToHashSet(StringComparer.Ordinal);
        var selected = direct
            .Where(node => picked.Contains(node.ProposalId))
            .Select(node => new ResolvedRequestProposalNode(
                node,
                TargetFor(node, expectedDescriptor, targets).ExternalIdentity))
            .ToArray();
        if (selected.Length != picked.Count) {
            throw new RequestCommitValidationException(
                "Every selected proposal must be a requestable direct child of the reviewed root.");
        }

        return selected;
    }

    private static RequestReviewTarget TargetFor(
        EntityMetadataProposal node,
        RequestKindDescriptor expectedDescriptor,
        IReadOnlyDictionary<string, RequestReviewTarget> targets) {
        if (!targets.TryGetValue(node.ProposalId, out var target)
            || target.Kind != expectedDescriptor.Kind
            || target.EntityKind != node.TargetKind.ToEntityKind()
            || !target.Requestable) {
            throw new RequestCommitValidationException(
                $"Proposal '{node.ProposalId}' is not a requestable '{expectedDescriptor.Kind.ToCode()}' target.");
        }

        return target;
    }
}
