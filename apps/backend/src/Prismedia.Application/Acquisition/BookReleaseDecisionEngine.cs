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

/// <summary>Rejects releases whose named format is not in the profile's allowed set. Titles that name no format pass.</summary>
public sealed class FormatSpecification : IReleaseSpecification {
    public ReleaseRejectionReason Reason => ReleaseRejectionReason.UnsupportedFormat;

    public ReleaseRejectionReason? Evaluate(IndexerRelease release, BookAcquisitionRules rules) {
        if (rules.AllowedFormats.Count == 0) {
            return null;
        }

        var detected = BookFormatDetection.Detect(release.Title);
        if (detected.Count == 0) {
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

/// <summary>Scores and filters indexer releases against a book acquisition profile's rules.</summary>
public interface IBookReleaseDecisionEngine {
    /// <summary>
    /// Evaluates each release against the rules and returns scored verdicts ordered best-first.
    /// Accepted candidates (highest score first) precede rejected ones so a review UI can preselect the top accepted release.
    /// </summary>
    IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules);
}

/// <summary>Default decision engine: runs every specification, then ranks accepted releases by a seeder-weighted score.</summary>
public sealed class BookReleaseDecisionEngine : IBookReleaseDecisionEngine {
    private static readonly IReleaseSpecification[] Specifications = [
        new ProtocolSpecification(),
        new FormatSpecification(),
        new MinSeedersSpecification(),
        new SizeSpecification(),
        new RequiredTermsSpecification(),
        new IgnoredTermsSpecification(),
        new LanguageSpecification()
    ];

    public IReadOnlyList<ScoredRelease> Evaluate(
        IReadOnlyList<(IndexerRelease Release, Guid? IndexerConfigId, string IndexerName)> releases,
        BookAcquisitionRules rules) {
        var scored = new List<ScoredRelease>(releases.Count);
        foreach (var (release, indexerConfigId, indexerName) in releases) {
            var rejections = new List<ReleaseRejectionReason>();
            foreach (var specification in Specifications) {
                if (specification.Evaluate(release, rules) is { } reason) {
                    rejections.Add(reason);
                }
            }

            scored.Add(new ScoredRelease(
                release,
                indexerConfigId,
                indexerName,
                rejections.Count == 0,
                Score(release),
                rejections));
        }

        // Accepted first, then by descending score; rejected releases trail but keep their score for display.
        return scored
            .OrderByDescending(candidate => candidate.Accepted)
            .ThenByDescending(candidate => candidate.Score)
            .ToArray();
    }

    /// <summary>
    /// Composite ranking score. Seeders dominate (log-scaled so a 1000-seed release does not bury a healthy
    /// 50-seed one), with peers as a small tiebreak. Deterministic and independent of wall-clock time.
    /// </summary>
    private static double Score(IndexerRelease release) {
        var seeders = Math.Max(release.Seeders ?? 0, 0);
        var peers = Math.Max(release.Peers ?? 0, 0);
        return (Math.Log10(seeders + 1) * 100) + (Math.Min(peers, 100) * 0.25);
    }
}
