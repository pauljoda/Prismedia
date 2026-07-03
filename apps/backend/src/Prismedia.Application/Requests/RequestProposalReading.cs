using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

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

    /// <summary>
    /// The best-ranked image of one image role (poster, backdrop — providers emit the entity image-role
    /// vocabulary), or null when the proposal carries none of that role.
    /// </summary>
    public static string? BestImageOfKind(EntityMetadataProposal proposal, string kind) =>
        proposal.Images
            .Where(image => string.Equals(image.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(image => image.Rank ?? 0)
            .Select(image => image.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

    /// <summary>
    /// Runtime in minutes from the patch's open stats vocabulary, or null. Providers report either
    /// minutes or seconds; this is the single decode site for those keys.
    /// </summary>
    public static int? RuntimeMinutesOf(EntityMetadataPatch patch) {
        // prism-vocab: external (open plugin stats vocabulary)
        if (patch.Stats.TryGetValue("runtimeMinutes", out var minutes) && minutes > 0) {
            return minutes;
        }

        if (patch.Stats.TryGetValue("runtimeSeconds", out var seconds) && seconds > 0) {
            return Math.Max(1, (int)Math.Round(seconds / 60.0));
        }

        return null;
    }

    /// <summary>
    /// Cast members in billed order, each with the character/role line and a headshot resolved from the
    /// proposal's person relationship nodes (providers ship them alongside the credits). Performing
    /// credit roles show their character; crew roles (director, writer, author, …) show the role itself.
    /// </summary>
    public static IReadOnlyList<RequestCastMember> CastOf(EntityMetadataProposal proposal) {
        if (proposal.Patch is not { } patch) {
            return [];
        }

        var headshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in proposal.Relationships ?? []) {
            if (node.TargetKind == ProposalKind.Person && node.Patch?.Title is { Length: > 0 } name && BestImage(node) is { } image) {
                headshots.TryAdd(name.Trim(), image);
            }
        }

        return patch.Credits
            .Where(credit => !string.IsNullOrWhiteSpace(credit.Name))
            .OrderBy(credit => credit.SortOrder ?? int.MaxValue)
            .Select(credit => new RequestCastMember(
                credit.Name.Trim(),
                RoleLine(credit),
                headshots.GetValueOrDefault(credit.Name.Trim())))
            .ToArray();
    }

    /// <summary>The subtitle line for a credit: the character when known, else the humanized crew role, else null for plain cast.</summary>
    private static string? RoleLine(CreditPatch credit) {
        if (!string.IsNullOrWhiteSpace(credit.Character)) {
            return credit.Character.Trim();
        }

        // prism-vocab: external (open plugin credit-role vocabulary) — performing roles carry no label of their own.
        var role = credit.Role.Trim();
        if (role.Length == 0 || role.Equals("cast", StringComparison.OrdinalIgnoreCase)
            || role.Equals("actor", StringComparison.OrdinalIgnoreCase)
            || role.Equals("guest", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return char.ToUpperInvariant(role[0]) + role[1..];
    }

    /// <summary>The season number a proposal declares (a season's own, or the season an episode belongs to), or null.</summary>
    public static int? SeasonNumberOf(EntityMetadataPatch patch) =>
        PositionOf(patch, EntityPositionCodes.Season, "seasonNumber");

    /// <summary>The episode number an episode proposal declares, or null.</summary>
    public static int? EpisodeNumberOf(EntityMetadataPatch patch) =>
        PositionOf(patch, EntityPositionCodes.Episode, "episodeNumber");

    /// <summary>Reads a position by its canonical code or its provider wire spelling. prism-vocab: external (plugin positions vocabulary).</summary>
    private static int? PositionOf(EntityMetadataPatch patch, params string[] keys) {
        foreach (var (code, value) in patch.Positions) {
            if (keys.Any(key => key.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase))) {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// An album proposal's track children projected to review tracks: album-global number (provider
    /// sort order when present), title, and duration when known.
    /// </summary>
    public static IReadOnlyList<RequestTrack> TracksOf(EntityMetadataProposal proposal) =>
        proposal.Children
            .Where(node => node.TargetKind == ProposalKind.AudioTrack && node.Patch is not null)
            .Select((node, index) => {
                // prism-vocab: external (open plugin positions/stats vocabulary)
                var patch = node.Patch!;
                var number = patch.Positions.TryGetValue("sortOrder", out var order) ? order + 1 : index + 1;
                int? duration = patch.Stats.TryGetValue("runtimeSeconds", out var seconds) && seconds > 0 ? seconds : null;
                return new RequestTrack(number, string.IsNullOrWhiteSpace(patch.Title) ? $"Track {index + 1}" : patch.Title!, duration);
            })
            .ToArray();
}
