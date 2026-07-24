using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Protects Prismedia's canonical identity store from provider proposal fields that do not uniquely
/// identify one structural entity. A repeated identity pair is still useful provider context, but it
/// cannot be persisted as entity identity evidence because resolution treats every supplied pair as
/// authoritative.
/// </summary>
public static class EntityMetadataProposalIdentityPolicy {
    /// <summary>
    /// Removes identity pairs repeated by structural nodes of the same kind. Relationship nodes are
    /// normalized as independent subtrees because the same related person or studio may legitimately
    /// appear more than once in a proposal graph.
    /// </summary>
    /// <param name="proposal">Provider-authored proposal tree.</param>
    /// <returns>A proposal tree whose structural identities identify at most one node per kind.</returns>
    public static EntityMetadataProposal RemoveSharedStructuralIdentities(EntityMetadataProposal proposal) {
        ArgumentNullException.ThrowIfNull(proposal);
        var occurrences = new Dictionary<StructuralIdentity, int>();
        CountStructuralIdentities(proposal, occurrences);
        var shared = occurrences
            .Where(pair => pair.Value > 1)
            .Select(pair => pair.Key)
            .ToHashSet();
        if (shared.Count == 0) {
            return proposal;
        }

        return SanitizeStructuralTree(proposal, shared);
    }

    private static void CountStructuralIdentities(
        EntityMetadataProposal node,
        IDictionary<StructuralIdentity, int> occurrences) {
        if (!node.TargetKind.IsRelationship()) {
            foreach (var identity in ValidIdentities(node)) {
                var key = new StructuralIdentity(node.TargetKind, identity);
                occurrences[key] = occurrences.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        foreach (var child in node.Children ?? []) {
            if (!child.TargetKind.IsRelationship()) {
                CountStructuralIdentities(child, occurrences);
            }
        }
    }

    private static EntityMetadataProposal SanitizeStructuralTree(
        EntityMetadataProposal node,
        IReadOnlySet<StructuralIdentity> shared) {
        var patch = node.Patch;
        if (!node.TargetKind.IsRelationship() && node.Patch?.ExternalIds is { } externalIds) {
            patch = node.Patch with {
                ExternalIds = externalIds
                    .Where(pair => !IsShared(node.TargetKind, pair, shared))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            };
        }

        var children = (node.Children ?? [])
            .Select(child => child.TargetKind.IsRelationship()
                ? RemoveSharedStructuralIdentities(child)
                : SanitizeStructuralTree(child, shared))
            .ToArray();
        var relationships = (node.Relationships ?? [])
            .Select(RemoveSharedStructuralIdentities)
            .ToArray();
        return node with { Patch = patch, Children = children, Relationships = relationships };
    }

    private static bool IsShared(
        ProposalKind kind,
        KeyValuePair<string, string> pair,
        IReadOnlySet<StructuralIdentity> shared) =>
        TryIdentity(pair, out var identity) &&
        shared.Contains(new StructuralIdentity(kind, identity));

    private static IEnumerable<ExternalIdentity> ValidIdentities(EntityMetadataProposal node) {
        if (node.Patch?.ExternalIds is not { } externalIds) {
            yield break;
        }

        foreach (var pair in externalIds) {
            if (TryIdentity(pair, out var identity)) {
                yield return identity;
            }
        }
    }

    private static bool TryIdentity(
        KeyValuePair<string, string> pair,
        out ExternalIdentity identity) {
        try {
            identity = new ExternalIdentity(pair.Key, pair.Value);
            return true;
        } catch (ArgumentException) {
            identity = null!;
            return false;
        }
    }

    private sealed record StructuralIdentity(ProposalKind Kind, ExternalIdentity Identity);
}
