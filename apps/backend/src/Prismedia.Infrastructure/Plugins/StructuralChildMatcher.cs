using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

internal static class StructuralChildMatcher {
    public static EntityMetadataProposal? FindProviderChild(
        StructuralLocalChild localChild,
        IReadOnlyList<EntityMetadataProposal> providerChildren,
        ISet<int> usedProviderIndexes,
        bool cautious) {
        var bestIndex = -1;
        var bestScore = 0;
        for (var index = 0; index < providerChildren.Count; index++) {
            if (usedProviderIndexes.Contains(index)) {
                continue;
            }

            var score = ScoreLocalToProvider(localChild, providerChildren[index], cautious);
            if (score > bestScore) {
                bestIndex = index;
                bestScore = score;
            }
        }

        if (bestIndex < 0) {
            return null;
        }

        usedProviderIndexes.Add(bestIndex);
        return providerChildren[bestIndex];
    }

    public static StructuralLocalChild? FindLocalChild(
        EntityMetadataProposal providerChild,
        IReadOnlyList<StructuralLocalChild> localChildren,
        ISet<Guid> usedLocalEntityIds,
        bool cautious) {
        StructuralLocalChild? bestChild = null;
        var bestScore = 0;
        foreach (var localChild in localChildren) {
            if (usedLocalEntityIds.Contains(localChild.EntityId)) {
                continue;
            }

            var score = ScoreLocalToProvider(localChild, providerChild, cautious);
            if (score > bestScore) {
                bestChild = localChild;
                bestScore = score;
            }
        }

        if (bestChild is null) {
            return null;
        }

        usedLocalEntityIds.Add(bestChild.EntityId);
        return bestChild;
    }

    public static bool IsSameLocalAndProviderChild(
        StructuralLocalChild localChild,
        EntityMetadataProposal providerChild,
        bool cautious = false) =>
        ScoreLocalToProvider(localChild, providerChild, cautious) > 0;

    public static bool IsSameProposalChild(EntityMetadataProposal left, EntityMetadataProposal right) {
        if (!AreCompatibleProposalKinds(left.TargetKind, right.TargetKind)) {
            return false;
        }

        if (left.TargetEntityId is { } leftId && right.TargetEntityId is { } rightId) {
            return leftId == rightId;
        }

        var leftSortOrder = StructuralSortOrder(left);
        var rightSortOrder = StructuralSortOrder(right);
        if (leftSortOrder is not null || rightSortOrder is not null) {
            return leftSortOrder is not null &&
                rightSortOrder is not null &&
                leftSortOrder == rightSortOrder;
        }

        return TitleCompatibility(left.Patch.Title, right.Patch.Title) == StructuralTitleCompatibility.Exact;
    }

    public static int? StructuralSortOrder(EntityMetadataProposal child) =>
        EntityMetadataPositionRules.SortOrderFor(
            child.TargetKind.ToEntityKind().ToCode(),
            EntityMetadataPositionRules.Normalize(child.Patch.Positions));

    public static bool AreCompatibleProposalKinds(ProposalKind leftKind, ProposalKind rightKind) =>
        leftKind.ToEntityKind() == rightKind.ToEntityKind();

    public static bool IsCompatibleStructuralKind(string localKind, ProposalKind proposalKind) =>
        localKind == proposalKind.ToEntityKind().ToCode();

    private static int ScoreLocalToProvider(
        StructuralLocalChild localChild,
        EntityMetadataProposal providerChild,
        bool cautious) {
        if (!IsCompatibleStructuralKind(localChild.KindCode, providerChild.TargetKind)) {
            return 0;
        }

        if (providerChild.TargetEntityId is { } targetEntityId && targetEntityId == localChild.EntityId) {
            return 100;
        }

        var providerSortOrder = EntityMetadataPositionRules.SortOrderFor(
            localChild.KindCode,
            EntityMetadataPositionRules.Normalize(providerChild.Patch.Positions));
        var numbersMatch = localChild.SortOrder is { } localSortOrder &&
            providerSortOrder is { } matchedSortOrder &&
            localSortOrder == matchedSortOrder;
        var titleCompatibility = TitleCompatibility(localChild.Title, providerChild.Patch.Title);
        var titlesCompatible = titleCompatibility != StructuralTitleCompatibility.None;
        if (titlesCompatible && numbersMatch) {
            return 90;
        }

        if (titleCompatibility == StructuralTitleCompatibility.Exact) {
            return cautious ? 80 : 50;
        }

        if (titleCompatibility == StructuralTitleCompatibility.Contains) {
            return cautious ? 70 : 45;
        }

        if (!numbersMatch) {
            return 0;
        }

        return cautious && HasUsefulTitle(localChild.Title) && HasUsefulTitle(providerChild.Patch.Title)
            ? 0
            : 40;
    }

    private static StructuralTitleCompatibility TitleCompatibility(string? left, string? right) {
        var leftTokens = NormalizeTitleTokens(left);
        var rightTokens = NormalizeTitleTokens(right);
        if (leftTokens.Length == 0 || rightTokens.Length == 0) {
            return StructuralTitleCompatibility.None;
        }

        if (leftTokens.SequenceEqual(rightTokens, StringComparer.OrdinalIgnoreCase)) {
            return StructuralTitleCompatibility.Exact;
        }

        return ContainsTokenSequence(leftTokens, rightTokens) || ContainsTokenSequence(rightTokens, leftTokens)
            ? StructuralTitleCompatibility.Contains
            : StructuralTitleCompatibility.None;
    }

    private static bool HasUsefulTitle(string? value) {
        var tokens = NormalizeTitleTokens(value);
        return tokens.Length > 0 && !IsGenericStructuralTitle(tokens);
    }

    private static string[] NormalizeTitleTokens(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool ContainsTokenSequence(string[] haystack, string[] needle) {
        if (needle.Length == 0 || needle.Length > haystack.Length) {
            return false;
        }

        for (var start = 0; start <= haystack.Length - needle.Length; start++) {
            var matches = true;
            for (var offset = 0; offset < needle.Length; offset++) {
                if (!haystack[start + offset].Equals(needle[offset], StringComparison.OrdinalIgnoreCase)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericStructuralTitle(string[] tokens) {
        var sawStructuralNumber = false;
        foreach (var token in tokens.Select(CleanTitleToken).Where(token => token.Length > 0)) {
            if (IsStructuralNumberToken(token)) {
                sawStructuralNumber = true;
                continue;
            }

            if (IsGenericStructuralWord(token)) {
                continue;
            }

            return false;
        }

        return sawStructuralNumber;
    }

    private static string CleanTitleToken(string token) =>
        token.Trim('.', ',', ':', ';', '-', '_', '[', ']', '(', ')');

    private static bool IsStructuralNumberToken(string token) =>
        token.All(char.IsDigit) || LooksLikeSeasonEpisodeToken(token);

    private static bool LooksLikeSeasonEpisodeToken(string token) {
        if (token.Length < 4 || token[0] is not ('s' or 'S')) {
            return false;
        }

        var episodeMarker = token.IndexOf('e', StringComparison.OrdinalIgnoreCase);
        return episodeMarker > 1 &&
            episodeMarker < token.Length - 1 &&
            token[1..episodeMarker].All(char.IsDigit) &&
            token[(episodeMarker + 1)..].All(char.IsDigit);
    }

    private static bool IsGenericStructuralWord(string token) =>
        token.Equals("local", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("episode", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("ep", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("season", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("series", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("track", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("song", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("volume", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("vol", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("chapter", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("page", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("part", StringComparison.OrdinalIgnoreCase);

    private enum StructuralTitleCompatibility {
        None,
        Contains,
        Exact
    }
}

internal sealed record StructuralLocalChild(Guid EntityId, string KindCode, string Title, int? SortOrder);
