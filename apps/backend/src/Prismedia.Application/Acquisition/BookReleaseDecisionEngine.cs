using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// A single rule the decision engine applies to a release. Returns the reason the release fails the
/// rule, or null when it satisfies the rule. Modeled on Sonarr's specification pattern but reduced to
/// a pure function with no I/O.
/// </summary>
public interface IReleaseSpecification {
    /// <summary>The rejection reason this specification reports, used for documentation and ordering.</summary>
    ReleaseRejectionReason Reason { get; }

    /// <summary>Returns <see cref="Reason"/> when the release violates the rule, otherwise null.</summary>
    ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules);
}

/// <summary>Rejects releases that use a protocol Prismedia does not acquire in v1 (anything but torrent).</summary>
public sealed class ProtocolSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.WrongProtocol;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) =>
        release.Protocol == DownloadProtocol.Torrent ? null : Reason;
}

/// <summary>
/// Rejects releases the importer can't handle. A title naming only an unimportable format (CBR/RAR/MOBI/AZW)
/// is rejected up front so it is never downloaded only to dead-end at import. When a profile restricts
/// formats, a title naming an importable format outside that set is also rejected. Titles that name no
/// recognizable format pass — the actual payload is checked at import.
/// </summary>
public sealed class FormatSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.UnsupportedFormat;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        var detected = BookFormatDetection.Detect(release.Title);
        if (detected.Count == 0) {
            // No importable format named — reject only if the title declares an unimportable format.
            return BookFormatDetection.NamesUnsupportedFormat(release.Title) ? Reason : null;
        }

        if (rules.AllowedFormats.Count == 0) {
            return null;
        }

        return detected.Overlaps(rules.AllowedFormats) ? null : Reason;
    }
}

/// <summary>Rejects torrents with fewer seeders than the profile minimum. Releases with unknown seeders pass.</summary>
public sealed class MinSeedersSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.BelowMinSeeders;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (release.Seeders is not { } seeders) {
            return null;
        }

        return seeders >= rules.MinSeeders ? null : Reason;
    }
}

/// <summary>Rejects releases whose size falls outside the profile's allowed range.</summary>
public sealed class SizeSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.SizeOutOfRange;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.MinSizeBytes is { } min && release.SizeBytes < min) {
            return Reason;
        }

        if (rules.MaxSizeBytes is { } max && release.SizeBytes > max) {
            return Reason;
        }

        return null;
    }
}

/// <summary>Rejects releases missing any profile-required term in the title.</summary>
public sealed class RequiredTermsSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.MissingRequiredTerm;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        foreach (var term in rules.RequiredTerms) {
            if (!release.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) {
                return Reason;
            }
        }

        return null;
    }
}

/// <summary>Rejects releases containing any profile-ignored term in the title.</summary>
public sealed class IgnoredTermsSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.HasIgnoredTerm;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        foreach (var term in rules.IgnoredTerms) {
            if (release.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) {
                return Reason;
            }
        }

        return null;
    }
}

/// <summary>
/// Rejects releases with no path to a download at all. A direct download/magnet link passes; so does a
/// release that only has an info page, because the link can be resolved from that page (or the user can
/// upload the .torrent manually). Only a release with neither is rejected.
/// </summary>
public sealed class DownloadLinkSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.NoDownloadLink;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) =>
        !string.IsNullOrWhiteSpace(release.DownloadUrl)
        || !string.IsNullOrWhiteSpace(release.MagnetUrl)
        || !string.IsNullOrWhiteSpace(release.InfoUrl)
            ? null
            : Reason;
}

/// <summary>Rejects releases whose language does not match a profile-required language.</summary>
public sealed class LanguageSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.LanguageMismatch;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (string.IsNullOrWhiteSpace(rules.Language) || string.IsNullOrWhiteSpace(release.Language)) {
            return null;
        }

        return string.Equals(release.Language, rules.Language, StringComparison.OrdinalIgnoreCase) ? null : Reason;
    }
}

/// <summary>
/// Scores and filters indexer releases for one media kind. Resolved per <see cref="Kind"/> through
/// <see cref="IAcquisitionDecisionEngineFactory"/> so additional kinds (video, audio) register their own
/// engine without the search runner changing. (The rules type is still book-specific; generalizing it is
/// a later step — this slice only establishes the kind-dispatch seam.)
/// </summary>
public interface IAcquisitionDecisionEngine {
    /// <summary>The media kind this engine scores releases for.</summary>
    EntityKind Kind { get; }

    /// <summary>
    /// Evaluates each release against the rules and returns scored verdicts ordered best-first.
    /// Accepted candidates (highest score first) precede rejected ones so a review UI can preselect the top accepted release.
    /// </summary>
    /// <param name="blocklistedIdentities">
    /// Normalized release identities (see <see cref="ReleaseIdentity"/>) that have been blocklisted. A release whose
    /// identity is in this set is rejected with <see cref="ReleaseRejectionReason.Blocklisted"/> so failed-download
    /// auto-recovery never re-grabs a release a prior attempt already failed. Null/empty means no blocklist applies.
    /// </param>
    IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null);
}

/// <summary>Default book decision engine: runs every specification, then ranks accepted releases by a seeder-weighted score.</summary>
public sealed class BookReleaseDecisionEngine : IAcquisitionDecisionEngine {
    public EntityKind Kind => EntityKind.Book;

    private static readonly IReleaseSpecification[] Specifications = [
        new ProtocolSpecification(),
        new DownloadLinkSpecification(),
        new FormatSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification()
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules,
        IReadOnlySet<string>? blocklistedIdentities = null) {
        var scored = new List<ScoredRelease>(releases.Count);
        foreach (var (release, indexerConfigId, indexerName) in releases) {
            var rejections = new List<ReleaseRejectionReason>();
            foreach (var specification in Specifications) {
                if (specification.Evaluate(release, rules) is { } reason) {
                    rejections.Add(reason);
                }
            }

            // The blocklist gate is the dynamic-state analog of a specification: it depends on the
            // current blocklist (not the static profile rules), so it is applied here rather than in
            // the rules-only Specifications array. Same transparent-rejection outcome.
            if (blocklistedIdentities is { Count: > 0 }
                && blocklistedIdentities.Contains(ReleaseIdentity.For(release.InfoHash, indexerName, release.Title))) {
                rejections.Add(ReleaseRejectionReason.Blocklisted);
            }

            scored.Add(new ScoredRelease(
                release,
                indexerConfigId,
                indexerName,
                rejections.Count == 0,
                Score(release, rules),
                rejections));
        }

        // Accepted first, then by descending score; rejected releases trail but keep their score for display.
        return scored
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();
    }

    /// <summary>
    /// Each preferred term found in a release title adds this much to the score — large enough that any
    /// preferred match outranks any seeder difference, so preference behaves like a quality tier (a release
    /// with more preferred terms is always ranked first), while seeders order releases within the same tier.
    /// </summary>
    private const double PreferredTermBoost = 1000;

    /// <summary>
    /// Composite ranking score. Preferred-term matches dominate (each is a full tier above seeders), then
    /// seeders (log-scaled so a 1000-seed release does not bury a healthy 50-seed one), with peers as a small
    /// tiebreak. Deterministic and independent of wall-clock time.
    /// </summary>
    private static double Score(IndexerRelease release, BookAcquisitionRules rules) {
        var seeders = Math.Max(release.Seeders ?? 0, 0);
        var peers = Math.Max(release.Peers ?? 0, 0);
        var preferred = rules.PreferredTerms.Count(term => release.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
        return (preferred * PreferredTermBoost) + (Math.Log10(seeders + 1) * 100) + (Math.Min(peers, 100) * 0.25);
    }
}

/// <summary>Resolves the decision engine for a media kind, mirroring the indexer/download-client factories.</summary>
public interface IAcquisitionDecisionEngineFactory {
    /// <summary>Returns the engine registered for <paramref name="kind"/>, or throws when none is registered.</summary>
    IAcquisitionDecisionEngine Get(EntityKind kind);
}

/// <summary>Dispatches to the registered <see cref="IAcquisitionDecisionEngine"/> for a kind (one engine per kind).</summary>
public sealed class AcquisitionDecisionEngineFactory(IEnumerable<IAcquisitionDecisionEngine> engines) : IAcquisitionDecisionEngineFactory {
    private readonly IReadOnlyDictionary<EntityKind, IAcquisitionDecisionEngine> _byKind =
        engines.ToDictionary(engine => engine.Kind);

    public IAcquisitionDecisionEngine Get(EntityKind kind) =>
        _byKind.TryGetValue(kind, out var engine)
            ? engine
            : throw new InvalidOperationException($"No acquisition decision engine is registered for kind '{kind}'.");
}
