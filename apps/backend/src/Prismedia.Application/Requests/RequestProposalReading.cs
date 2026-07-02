using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Requests;

/// <summary>
/// Shared readers for plugin metadata proposals in the request flow: provider-qualified id handling and
/// the small projections (work id, best image, year, author credit) the detail surface and the request
/// commit both need. One home so the two can never drift on how they read the same proposal.
/// </summary>
public static class RequestProposalReading {
    /// <summary>Splits a provider-qualified id ("provider:itemId") into its parts, or (null, null) when malformed.</summary>
    public static (string? ProviderId, string? ItemId) SplitProviderQualifiedId(string externalId) {
        if (string.IsNullOrWhiteSpace(externalId)) {
            return (null, null);
        }

        var separator = externalId.IndexOf(':');
        if (separator <= 0 || separator >= externalId.Length - 1) {
            return (null, null);
        }

        return (externalId[..separator], externalId[(separator + 1)..]);
    }

    /// <summary>The provider's own work id carried by a proposal (preferring the provider's key), or null.</summary>
    public static string? WorkIdFor(string providerId, EntityMetadataProposal proposal) {
        var workId = proposal.Patch?.ExternalIds.GetValueOrDefault(providerId)
            ?? proposal.Patch?.ExternalIds.Values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(workId) ? null : workId;
    }

    /// <summary>The provider-qualified id ("provider:workId") for a proposal, or null when it carries no work id.</summary>
    public static string? QualifiedIdFor(string providerId, EntityMetadataProposal proposal) =>
        WorkIdFor(providerId, proposal) is { } workId ? $"{providerId}:{workId}" : null;

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
}
