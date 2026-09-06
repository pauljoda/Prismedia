using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Bounds and deduplicates formal work titles before they become acquisition evidence or queries.</summary>
public static class AcquisitionWorkTitles {
    /// <summary>Maximum formal alternatives retained for one provider work.</summary>
    public const int MaximumTitles = 8;

    /// <summary>Maximum length of a useful work title, shared with metadata persistence.</summary>
    public const int MaximumTitleLength = 512;

    /// <summary>Keeps nonempty names in provider order and folds equivalent spelling/separator variants.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> titles) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var title in titles) {
            if (string.IsNullOrWhiteSpace(title) || title.Length > MaximumTitleLength) continue;
            var key = string.Join(' ', ReleaseTitleIdentity.ComparableTokens(title));
            if (key.Length == 0 || !seen.Add(key)) continue;
            result.Add(title.Trim());
            if (result.Count == MaximumTitles) break;
        }
        return result;
    }

    /// <summary>Builds equivalent query inputs while preserving the work's year and canonical episode coordinates.</summary>
    public static IReadOnlyList<AcquisitionSearchInput> QueryInputs(AcquisitionSearchInput input) {
        var family = EntityKindRegistry.Describe(AcquisitionProfileKinds.For(input.Kind)).AcquisitionProfile?.NamingFamily;
        if (family is not (AcquisitionNamingFamily.Movie or AcquisitionNamingFamily.Television)
            || input.AlternativeWorkTitles.Count == 0) return [input];
        return Normalize(input.AlternativeWorkTitles.Prepend(input.WorkTitle))
            .Select(title => family == AcquisitionNamingFamily.Television ? input with { Series = title } : input with { Title = title })
            .ToArray();
    }

    /// <summary>Offers at most one Latin-accent spelling fallback per formal video-work name, preserving all request coordinates.</summary>
    public static IReadOnlyList<AcquisitionSearchInput> AccentFallbackQueryInputs(AcquisitionSearchInput input) {
        var family = EntityKindRegistry.Describe(AcquisitionProfileKinds.For(input.Kind)).AcquisitionProfile?.NamingFamily;
        if (family is not (AcquisitionNamingFamily.Movie or AcquisitionNamingFamily.Television)) return [];
        return QueryInputs(input)
            .Select(variant => (Input: variant, Title: ReleaseTitleText.FoldLatinAccents(variant.WorkTitle)))
            .Where(variant => !string.Equals(variant.Title, variant.Input.WorkTitle, StringComparison.Ordinal))
            .Select(variant => family == AcquisitionNamingFamily.Television
                ? variant.Input with { Series = variant.Title }
                : variant.Input with { Title = variant.Title })
            .ToArray();
    }

    /// <summary>Tests canonical and formally equivalent names with the same strict identity boundary.</summary>
    public static ReleaseTitleIdentity.Result Match(string candidate, string? primary, IReadOnlyList<string> alternatives,
        TvEpisodeIdentifierSet? episode = null) {
        ReleaseTitleIdentity.Result MatchOne(string? title) => episode is null
            ? ReleaseTitleIdentity.Match(candidate, title)
            : ReleaseTitleIdentity.MatchWithEpisodeIdentifiers(candidate, title, episode);
        var result = MatchOne(primary);
        if (result.TitleMatched) return result;
        foreach (var title in alternatives) {
            var alternate = MatchOne(title);
            if (alternate.TitleMatched) return alternate;
        }
        return result;
    }

    /// <summary>Removes the longest matching formal parent name before reading episode evidence.</summary>
    public static string EpisodeEvidence(string candidate, string? primary, IReadOnlyList<string> alternatives) {
        var result = ReleaseTitleIdentity.WithoutLeadingWorkTitle(candidate, primary);
        foreach (var title in alternatives) {
            var suffix = ReleaseTitleIdentity.WithoutLeadingWorkTitle(candidate, title);
            if (suffix.Length < result.Length) result = suffix;
        }
        return result;
    }
}
