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

/// <summary>
/// Rejects releases whose transfer protocol no enabled download client can acquire (e.g. a usenet
/// release when no usenet client is configured). The allowed set is computed from the configured
/// clients at search time and carried on the rules.
/// </summary>
public sealed class ProtocolSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.WrongProtocol;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) =>
        rules.AllowedProtocols.Contains(release.Protocol) ? null : Reason;
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

/// <summary>
/// Rejects releases that declare a language (indexer attribute or a title token like FRENCH/GERMAN/ITA)
/// none of which is in the profile's preferred-language list. Unmarked releases pass — by convention only
/// non-English (or multi) audio gets tagged, so no declaration is assumed to be the top preference — and
/// multi-language releases satisfy any preference. An empty preferred list disables the gate.
/// </summary>
public sealed class LanguageSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.LanguageMismatch;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.PreferredLanguages.Count == 0) {
            return null;
        }

        var declared = ReleaseLanguageDetection.Detect(release.Title, release.Language);
        if (declared.Count == 0 || declared.Contains(ReleaseLanguageDetection.Multi)) {
            return null;
        }

        return rules.PreferredLanguages.Any(preferred => declared.Contains(ReleaseLanguageDetection.Canonicalize(preferred)))
            ? null
            : Reason;
    }
}

/// <summary>
/// Rejects releases whose detected quality falls below the profile's minimum-quality floor on either axis.
/// Off by default: the default <see cref="BookAcquisitionRules.MinQuality"/> is <see cref="BookQualityRank.Floor"/>,
/// which nothing is below. A title that names no quality scores at the floor, so a floor of Floor never rejects.
/// </summary>
public sealed class QualityFloorSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.QualityNotAllowed;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        var quality = BookFormatDetection.DetectQuality(release.Title);
        var min = rules.MinQuality;
        return quality.Source >= min.Source && quality.Format >= min.Format ? null : Reason;
    }
}

/// <summary>
/// On an upgrade search, rejects any candidate that is not a strict improvement over the owned copy. Gated on
/// <see cref="BookAcquisitionRules.IsUpgradeSearch"/> (not on the owned-quality value), so initial grabs are
/// never affected and a genuinely-unknown owned quality can't silently disable the gate. When the owned source
/// is unknown (the owned file's provenance could not be parsed), a source-only gain is NOT trusted to authorize
/// a replacement — only a verifiable format improvement counts — matching the conservative-replace policy.
/// </summary>
public sealed class UpgradeSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.NotAnUpgrade;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (!rules.IsUpgradeSearch) {
            return null;
        }

        var owned = rules.OwnedQuality;
        var candidate = BookFormatDetection.DetectQuality(release.Title);
        if (!candidate.StrictlyDominates(owned)) {
            return Reason;
        }

        // Conservative-replace: an unknown owned source might already be retail (just unparsed), so a gain
        // that comes only from the source axis is not trusted to auto-replace; require a real format win.
        if (owned.Source == BookSourceTier.Unknown && candidate.Format <= owned.Format) {
            return Reason;
        }

        return null;
    }
}

/// <summary>
/// On an upgrade search, rejects any candidate whose declared format tier is below the owned copy's, even if
/// its source improves — an explicit guard so a higher-source/lower-format release (e.g. a retail PDF over a
/// web EPUB) can never replace the owned file. Gated on <see cref="BookAcquisitionRules.IsUpgradeSearch"/>. A
/// title that names no format makes no downgrade claim, so it passes here and is judged by
/// <see cref="UpgradeSpecification"/> (which rejects it as <see cref="ReleaseRejectionReason.NotAnUpgrade"/>) —
/// avoiding a misleading downgrade reason for a format-anonymous title.
/// </summary>
public sealed class FormatFloorSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.FormatDowngrade;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (!rules.IsUpgradeSearch) {
            return null;
        }

        var tier = BookFormatDetection.DetectFormatTier(release.Title);
        if (tier == BookFormatTier.Unknown) {
            return null;
        }

        return tier < rules.OwnedQuality.Format ? Reason : null;
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
        // Quality gates run after importability: never accept a quality we cannot import. All three are
        // no-ops at the default Floor rules, so they only bite once a min-quality floor or an upgrade's
        // owned-quality is set.
        new QualityFloorSpecification(),
        new UpgradeSpecification(),
        new FormatFloorSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification(),
        new MinFormatScoreSpecification()
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
                BookReleaseScore.Of(release, rules),
                rejections));
        }

        // Accepted first, then by descending score; rejected releases trail but keep their score for display.
        return scored
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();
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
