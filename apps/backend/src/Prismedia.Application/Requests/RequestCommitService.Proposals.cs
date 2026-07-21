using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Identity and structural-proposal helpers for <see cref="RequestCommitService"/>.</summary>
public sealed partial class RequestCommitService {
    /// <summary>Every persistent identity a pick carries: the resolving provider's identity plus the proposal's external ids.</summary>
    private static IReadOnlyList<ExternalIdentity> IdentitiesOf(CommitPick pick) {
        var identities = new Dictionary<string, ExternalIdentity>(StringComparer.Ordinal);
        AddIdentity(identities, pick.Identity.Namespace, pick.Identity.Value);
        foreach (var (provider, value) in pick.Proposal.Patch?.ExternalIds ?? new Dictionary<string, string>()) {
            AddIdentity(identities, provider, value);
        }

        return identities.Values.ToArray();
    }

    private static void AddIdentity(
        IDictionary<string, ExternalIdentity> identities,
        string? identityNamespace,
        string? value) {
        if (TryIdentity(identityNamespace, value) is { } identity) {
            identities.TryAdd(identity.Namespace, identity);
        }
    }

    private static ExternalIdentity? TryIdentity(string? identityNamespace, string? value) {
        if (string.IsNullOrWhiteSpace(identityNamespace) || string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        try {
            return new ExternalIdentity(identityNamespace, value);
        } catch (ArgumentException) {
            // Plugin proposals can carry transient lookup URLs in their external-id bag. They are
            // useful while resolving the proposal but are not stable persisted identities.
            return null;
        }
    }

    /// <summary>The structural children whose identity-qualified ids were picked.</summary>
    private static IReadOnlyList<ResolvedRequestProposalNode> SelectStructuralChildren(
        string identityNamespace, EntityMetadataProposal proposal, IReadOnlyList<string> selectedChildIds) {
        var picked = selectedChildIds
            .Select(RequestProposalReading.ParseQualifiedIdentity)
            .Where(identity => identity is not null)
            .Select(identity => identity!)
            .ToHashSet();
        return proposal.Children
            .Where(child => !child.TargetKind.IsRelationship())
            .Select(child => (Proposal: child, Identity: TryIdentity(
                identityNamespace,
                RequestProposalReading.IdentityValueFor(identityNamespace, child))))
            .Where(item => item.Identity is not null && picked.Contains(item.Identity))
            .Select(item => new ResolvedRequestProposalNode(item.Proposal, item.Identity!))
            .ToArray();
    }

    private static IReadOnlyList<ResolvedRequestProposalNode> ResolveLegacyStructuralChildren(
        string identityNamespace,
        EntityMetadataProposal proposal) =>
        proposal.Children
            .Where(child => !child.TargetKind.IsRelationship())
            .Select(child => (Proposal: child, Identity: TryIdentity(
                identityNamespace,
                RequestProposalReading.IdentityValueFor(identityNamespace, child))))
            .Where(item => item.Identity is not null)
            .Select(item => new ResolvedRequestProposalNode(item.Proposal, item.Identity!))
            .ToArray();

    /// <summary>
    /// Direct structural children paired with the canonical identities selected from the plugin manifest.
    /// Proposal external-id bags can contain several namespaces and descendant namespaces commonly differ
    /// from their parent, so proposal id is the stable join between the reviewed tree and its targets.
    /// </summary>
    private static IReadOnlyList<ResolvedRequestProposalNode> ResolveReviewedStructuralChildren(
        EntityMetadataProposal proposal,
        IReadOnlyList<RequestReviewTarget> targets) {
        var identitiesByProposalId = targets
            .Where(target => target.Requestable)
            .GroupBy(target => target.ProposalId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().ExternalIdentity, StringComparer.Ordinal);
        return proposal.Children
            .Where(child => !child.TargetKind.IsRelationship())
            .Where(child => identitiesByProposalId.ContainsKey(child.ProposalId))
            .Select(child => new ResolvedRequestProposalNode(child, identitiesByProposalId[child.ProposalId]))
            .ToArray();
    }

    private static string TitleOr(string? title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
}
