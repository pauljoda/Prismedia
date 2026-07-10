using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// Shared readers for plugin metadata proposals in the request flow: identity-qualified id handling and
/// the small projections (work id, best image, year, author credit, structural position) used by review,
/// commit, monitoring, and metadata enrichment.
/// </summary>
public static class RequestProposalReading {
    /// <summary>Parses an identity-qualified id ("namespace:value"), or null when malformed.</summary>
    public static ExternalIdentity? ParseQualifiedIdentity(string externalId) {
        if (string.IsNullOrWhiteSpace(externalId)) {
            return null;
        }

        var separator = externalId.IndexOf(':');
        if (separator <= 0 || separator >= externalId.Length - 1) {
            return null;
        }

        try {
            return new ExternalIdentity(externalId[..separator], externalId[(separator + 1)..]);
        } catch (ArgumentException) {
            return null;
        }
    }

    /// <summary>The identity value a proposal carries for one canonical namespace, or null.</summary>
    public static string? IdentityValueFor(string identityNamespace, EntityMetadataProposal proposal) {
        var workId = proposal.Patch?.ExternalIds
            .FirstOrDefault(pair => pair.Key.Equals(identityNamespace, StringComparison.OrdinalIgnoreCase))
            .Value;
        return string.IsNullOrWhiteSpace(workId) ? null : workId;
    }

    /// <summary>The identity-qualified id for a proposal, or null when it carries no value in that namespace.</summary>
    public static string? QualifiedIdFor(string identityNamespace, EntityMetadataProposal proposal) =>
        IdentityValueFor(identityNamespace, proposal) is { } value
            ? FormatQualifiedIdentity(new ExternalIdentity(identityNamespace, value))
            : null;

    /// <summary>Formats a canonical persistent identity as "namespace:value".</summary>
    public static string FormatQualifiedIdentity(ExternalIdentity identity) =>
        $"{identity.Namespace}:{identity.Value}";

    /// <summary>The best-ranked image url a proposal carries (books generally return cover art), or null.</summary>
    public static string? BestImage(EntityMetadataProposal proposal) =>
        proposal.Images
            .OrderByDescending(image => image.Rank ?? 0)
            .Select(image => image.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

    /// <summary>Extracts a 4-digit year from any of the patch's date values (e.g. "2024" or "2024-03-26").</summary>
    public static int? YearFromDates(IReadOnlyDictionary<string, string> dates) {
        foreach (var value in dates.Values) {
            if (value is { Length: >= 4 } && int.TryParse(value[..4], out var year) && year is >= 1000 and <= 9999) {
                return year;
            }
        }

        return null;
    }

    /// <summary>The first credited author on a patch, or null. Credit roles are open provider vocabulary, so this matches loosely.</summary>
    public static string? AuthorFromCredits(EntityMetadataPatch patch) =>
        patch.Credits.FirstOrDefault(credit => credit.Role.Contains("author", StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>
    /// The first credited name of any role, or null — the generic "who made this" for kinds without an
    /// author concept (an album's artist, a movie's director), used to strengthen release search queries.
    /// </summary>
    public static string? PrimaryCredit(EntityMetadataPatch patch) =>
        patch.Credits.FirstOrDefault()?.Name;

    /// <summary>The season number a proposal declares (a season's own, or the season an episode belongs to), or null.</summary>
    public static int? SeasonNumberOf(EntityMetadataPatch patch) =>
        PositionOf(patch, EntityPositionCodes.Season, "seasonNumber");

    /// <summary>The volume number a proposal declares, or null.</summary>
    public static int? VolumeNumberOf(EntityMetadataPatch patch) =>
        PositionOf(patch, EntityPositionCodes.Volume, "volumeNumber");

    /// <summary>The episode number an episode proposal declares, or null.</summary>
    public static int? EpisodeNumberOf(EntityMetadataPatch patch) =>
        PositionOf(patch, EntityPositionCodes.Episode, "episodeNumber");

    /// <summary>
    /// Ordering number for a request child option in the vocabulary its parent selector understands:
    /// season children use seasonNumber (but season 0 / Specials is unnumbered for presets), book
    /// children use volumeNumber, episodes use episodeNumber, and generic children fall back to sortOrder.
    /// </summary>
    public static int? ChildNumberOf(RequestMediaKind childKind, EntityMetadataPatch patch) {
        var value = childKind switch {
            RequestMediaKind.Season => SeasonNumberOf(patch),
            RequestMediaKind.Episode => EpisodeNumberOf(patch),
            RequestMediaKind.Book => VolumeNumberOf(patch),
            _ => PositionOf(patch, EntityPositionCodes.Sort, "sortOrder"),
        };

        return childKind == RequestMediaKind.Season && value <= 0 ? null : value;
    }

    /// <summary>Reads a position by its canonical code or its provider wire spelling. prism-vocab: external (plugin positions vocabulary).</summary>
    private static int? PositionOf(EntityMetadataPatch patch, params string[] keys) {
        foreach (var (code, value) in patch.Positions) {
            if (keys.Any(key => key.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase))) {
                return value;
            }
        }

        return null;
    }

}
