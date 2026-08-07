using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Validates the cached review and user-filtered proposal submitted by the shared metadata review
/// surface. Request commits can therefore persist the already-reviewed data without another provider
/// lookup while still rejecting stale, mismatched, or expanded client payloads.
/// </summary>
internal static class ReviewedRequestProposalValidator {
    /// <summary>Returns a commit-ready review whose proposal is the validated user selection.</summary>
    public static RequestReviewResponse Validate(
        ReviewedRequestCommitRequest request,
        RequestReviewResponse review,
        EntityMetadataProposal selectedProposal) {
        if (RequestProposalRevision.Compute(review.Proposal) != request.ProposalRevision
            || !string.Equals(review.Revision, request.ProposalRevision, StringComparison.Ordinal)) {
            throw new RequestProposalChangedException();
        }
        if (!string.Equals(review.PluginId, request.PluginId, StringComparison.OrdinalIgnoreCase)
            || review.Kind != request.Kind
            || review.ExternalIdentity != request.RootExternalIdentity
            || review.Proposal.Patch is null) {
            throw new RequestCommitValidationException("The submitted review does not match the requested item.");
        }

        ValidateFilteredNode(review.Proposal, selectedProposal);
        ValidateRootSelections(request, selectedProposal);
        return review with { Proposal = selectedProposal };
    }

    private static void ValidateRootSelections(
        ReviewedRequestCommitRequest request,
        EntityMetadataProposal selectedProposal) {
        if (request.SelectedFields is null
            || request.SelectedFields.Any(string.IsNullOrWhiteSpace)
            || request.SelectedFields.Distinct(StringComparer.Ordinal).Count() != request.SelectedFields.Count
            || request.SelectedFields.Any(field => !field.TryDecodeAs<MetadataPatchField>(out _))) {
            throw new RequestCommitValidationException("Selected metadata fields must be known, non-empty, and unique.");
        }

        if (request.SelectedImages is null) {
            return;
        }

        var selectedImages = selectedProposal.Images ?? [];
        foreach (var (kind, url) in request.SelectedImages) {
            if (string.IsNullOrWhiteSpace(kind)
                || url is not null && !selectedImages.Any(image =>
                    string.Equals(image.Kind, kind, StringComparison.Ordinal)
                    && string.Equals(image.Url, url, StringComparison.Ordinal))) {
                throw new RequestCommitValidationException("Selected artwork must belong to the submitted proposal.");
            }
        }
    }

    private static void ValidateFilteredNode(
        EntityMetadataProposal reviewed,
        EntityMetadataProposal selected) {
        if (!string.Equals(reviewed.ProposalId, selected.ProposalId, StringComparison.Ordinal)
            || !string.Equals(reviewed.Provider, selected.Provider, StringComparison.OrdinalIgnoreCase)
            || reviewed.TargetKind != selected.TargetKind
            || reviewed.TargetEntityId != selected.TargetEntityId
            || selected.Patch is null) {
            throw new RequestCommitValidationException("The selected proposal does not match the reviewed proposal tree.");
        }

        ValidateFilteredPatch(reviewed.Patch, selected.Patch);
        ValidateSubset(reviewed.Images ?? [], selected.Images ?? [], "artwork");
        ValidateNodes(reviewed, selected);
    }

    private static void ValidateNodes(
        EntityMetadataProposal reviewed,
        EntityMetadataProposal selected) {
        var reviewedNodeList = (reviewed.Children ?? [])
            .Concat(reviewed.Relationships ?? [])
            .ToArray();
        if (reviewedNodeList.Any(node => string.IsNullOrWhiteSpace(node.ProposalId))
            || reviewedNodeList.Select(node => node.ProposalId).Distinct(StringComparer.Ordinal).Count()
                != reviewedNodeList.Length) {
            throw new RequestCommitValidationException(
                "Reviewed proposal ids must be non-empty and unique within each parent.");
        }
        var reviewedNodes = reviewedNodeList.ToDictionary(node => node.ProposalId, StringComparer.Ordinal);
        var selectedNodes = (selected.Children ?? [])
            .Concat(selected.Relationships ?? [])
            .ToArray();
        if (selectedNodes.Any(node => string.IsNullOrWhiteSpace(node.ProposalId))
            || selectedNodes.Select(node => node.ProposalId).Distinct(StringComparer.Ordinal).Count() != selectedNodes.Length) {
            throw new RequestCommitValidationException("Selected proposal ids must be non-empty and unique within each parent.");
        }

        foreach (var selectedNode in selectedNodes) {
            if (!reviewedNodes.TryGetValue(selectedNode.ProposalId, out var reviewedNode)) {
                throw new RequestCommitValidationException(
                    $"Proposal '{selectedNode.ProposalId}' was not part of the reviewed proposal.");
            }
            ValidateFilteredNode(reviewedNode, selectedNode);
        }
    }

    private static void ValidateFilteredPatch(EntityMetadataPatch reviewed, EntityMetadataPatch selected) {
        ValidateOptional(reviewed.Title, selected.Title);
        ValidateOptional(reviewed.Description, selected.Description);
        ValidateOptional(reviewed.Studio, selected.Studio);
        ValidateOptional(reviewed.Classification, selected.Classification);
        ValidateDictionary(reviewed.ExternalIds, selected.ExternalIds);
        ValidateSubset(reviewed.Urls, selected.Urls, "URL");
        ValidateSubset(reviewed.Tags, selected.Tags, "tag");
        ValidateSubset(reviewed.Credits, selected.Credits, "credit");
        ValidateDictionary(reviewed.Dates, selected.Dates);
        ValidateDictionary(reviewed.Stats, selected.Stats);
        ValidateDictionary(reviewed.Positions, selected.Positions);
        ValidateSubset(reviewed.DateEntries, selected.DateEntries, "date");

        if (reviewed.Rating != selected.Rating || reviewed.Flags != selected.Flags) {
            throw new RequestCommitValidationException("Non-selectable proposal state must match the reviewed proposal.");
        }
    }

    private static void ValidateOptional(string? reviewed, string? selected) {
        if (selected is not null && !string.Equals(reviewed, selected, StringComparison.Ordinal)) {
            throw new RequestCommitValidationException("Selected metadata must come from the reviewed proposal.");
        }
    }

    private static void ValidateDictionary<T>(
        IReadOnlyDictionary<string, T>? reviewed,
        IReadOnlyDictionary<string, T>? selected) {
        var reviewedValues = reviewed ?? new Dictionary<string, T>();
        foreach (var (key, value) in selected ?? new Dictionary<string, T>()) {
            if (!reviewedValues.TryGetValue(key, out var reviewedValue)
                || !EqualityComparer<T>.Default.Equals(reviewedValue, value)) {
                throw new RequestCommitValidationException("Selected metadata must come from the reviewed proposal.");
            }
        }
    }

    private static void ValidateSubset<T>(
        IReadOnlyList<T>? reviewed,
        IReadOnlyList<T>? selected,
        string label) {
        var remaining = (reviewed ?? []).ToList();
        foreach (var value in selected ?? []) {
            var index = remaining.FindIndex(candidate => EqualityComparer<T>.Default.Equals(candidate, value));
            if (index < 0) {
                throw new RequestCommitValidationException($"Selected {label} metadata must come from the reviewed proposal.");
            }
            remaining.RemoveAt(index);
        }
    }
}
