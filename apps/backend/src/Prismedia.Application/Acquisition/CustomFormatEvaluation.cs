using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// One condition of a custom format: a test of a single release axis. The <paramref name="Value"/> is
/// interpreted by <paramref name="Type"/> — a regex for the title/group types, a canonical language name
/// for <see cref="CustomFormatConditionType.Language"/>, and an exact ladder quality code for
/// <see cref="CustomFormatConditionType.Quality"/>. <paramref name="Negate"/> inverts the underlying test;
/// <paramref name="Required"/> makes the condition an AND-gate for the whole format (see
/// <see cref="CustomFormatEvaluation"/> for the full matching algebra).
/// </summary>
/// <param name="Type">The release axis this condition tests.</param>
/// <param name="Value">The pattern/name/code the axis is tested against, interpreted per <paramref name="Type"/>.</param>
/// <param name="Negate">When true, the condition matches when its underlying test does NOT.</param>
/// <param name="Required">When true, this condition must match for the format to match (a hard AND-gate).</param>
public sealed record CustomFormatCondition(CustomFormatConditionType Type, string Value, bool Negate, bool Required);

/// <summary>
/// A custom format resolved for scoring in one profile: its display name, the score this profile assigns
/// it, and the conditions that decide whether a release matches. The score lives with the format-in-profile
/// (mirroring Sonarr, where the same format is worth different points per profile); a format with score 0 is
/// never carried onto the rules.
/// </summary>
/// <param name="Name">The format's display name, used only for diagnostics.</param>
/// <param name="Score">The points added to a matching release's preference score (clamped ±10_000 at save).</param>
/// <param name="Conditions">The conditions whose collective match decides whether this format applies.</param>
public sealed record ScoredCustomFormat(string Name, int Score, IReadOnlyList<CustomFormatCondition> Conditions);

/// <summary>
/// Evaluates a release title against a profile's scored custom formats and sums the scores of every matching
/// format. Pure and deterministic (title truth only, no I/O), so the decision engine stays a pure function
/// of (releases, rules). Adapted from Sonarr's custom-format matching:
/// <list type="bullet">
/// <item>A format matches only when EVERY <see cref="CustomFormatCondition.Required"/> condition matches AND
/// at least one condition (of any kind) matches.</item>
/// <item>A <see cref="CustomFormatCondition.Negate"/> condition matches when its underlying test does NOT.</item>
/// <item>An invalid regex never throws at evaluation time — its condition simply never matches (compiled
/// defensively and cached).</item>
/// </list>
/// </summary>
public static class CustomFormatEvaluation {
    // Compiled-regex cache keyed by the raw pattern. A pattern that fails to compile caches a null so the
    // repeated evaluation of an invalid condition never re-attempts (and never throws). Bounded implicitly
    // by the small, user-authored set of distinct patterns across all formats.
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    /// <summary>
    /// The summed score of every custom format on <paramref name="rules"/> whose conditions match
    /// <paramref name="title"/>. Zero when no formats are configured or none match (including all-negative
    /// scores summing away). The kind governs how the quality condition places the title on a ladder.
    /// </summary>
    /// <param name="title">The release title to score.</param>
    /// <param name="rules">The profile rules carrying the scored custom formats (and the kind's ladder).</param>
    /// <param name="attributeLanguage">The indexer's language attribute, folded into language detection when present.</param>
    public static int Score(string title, BookAcquisitionRules rules, string? attributeLanguage = null) {
        if (rules.CustomFormats.Count == 0) {
            return 0;
        }

        var context = new ReleaseContext(title, rules.Kind, attributeLanguage);
        var total = 0;
        foreach (var format in rules.CustomFormats) {
            if (Matches(format, context)) {
                total += format.Score;
            }
        }

        return total;
    }

    /// <summary>Sonarr's match algebra: every Required condition matches AND at least one condition of any kind matches.</summary>
    private static bool Matches(ScoredCustomFormat format, ReleaseContext context) {
        if (format.Conditions.Count == 0) {
            return false;
        }

        var anyMatched = false;
        foreach (var condition in format.Conditions) {
            var matched = Evaluate(condition, context);
            if (condition.Required && !matched) {
                return false;
            }

            anyMatched |= matched;
        }

        return anyMatched;
    }

    /// <summary>Evaluates one condition against the release, applying the negate flip to the underlying test.</summary>
    private static bool Evaluate(CustomFormatCondition condition, ReleaseContext context) {
        var underlying = condition.Type switch {
            CustomFormatConditionType.ReleaseTitle => RegexMatches(condition.Value, context.Title),
            CustomFormatConditionType.ReleaseGroup => RegexMatches(condition.Value, context.ReleaseGroup),
            CustomFormatConditionType.Language => LanguageMatches(condition.Value, context),
            CustomFormatConditionType.Quality => QualityMatches(condition.Value, context),
            _ => false
        };

        return condition.Negate ? !underlying : underlying;
    }

    private static bool RegexMatches(string pattern, string? target) {
        if (string.IsNullOrEmpty(target) || Compile(pattern) is not { } regex) {
            return false;
        }

        return regex.IsMatch(target);
    }

    private static bool LanguageMatches(string value, ReleaseContext context) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var canonical = ReleaseLanguageDetection.Canonicalize(value);
        return context.Languages.Contains(canonical);
    }

    private static bool QualityMatches(string value, ReleaseContext context) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var (detected, _) = MediaQualityLadder.Detect(context.Kind, context.Title);
        return string.Equals(detected, value, StringComparison.Ordinal);
    }

    /// <summary>Compiles a regex case-insensitively, caching the result; a pattern that fails to compile caches null (never matches).</summary>
    private static Regex? Compile(string pattern) =>
        RegexCache.GetOrAdd(pattern, static raw => {
            try {
                return new Regex(raw, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            } catch (ArgumentException) {
                return null;
            }
        });

    /// <summary>Per-release detection results, computed once and shared across a format's conditions.</summary>
    private readonly struct ReleaseContext {
        public ReleaseContext(string title, EntityKind kind, string? attributeLanguage) {
            Title = title ?? string.Empty;
            Kind = kind;
            ReleaseGroup = ReleaseGroupDetection.Detect(Title);
            Languages = ReleaseLanguageDetection.Detect(Title, attributeLanguage);
        }

        public string Title { get; }
        public EntityKind Kind { get; }
        public string? ReleaseGroup { get; }
        public IReadOnlySet<string> Languages { get; }
    }
}
