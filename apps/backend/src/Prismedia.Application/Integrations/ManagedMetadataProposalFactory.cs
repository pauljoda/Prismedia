using System.Globalization;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Projects validated manager metadata into Prismedia's provider-neutral proposal shape.</summary>
public static class ManagedMetadataProposalFactory {
    private const string MatchReason = "Connected catalog";

    /// <summary>
    /// Builds a root proposal and its distinct people relationships from an exact manager candidate.
    /// Callers remain responsible for validating the manager evidence before projection.
    /// </summary>
    public static EntityMetadataProposal Create(
        Guid connectionId,
        string pluginId,
        ManagedCandidate candidate) {
        var rootIdentity = CanonicalRootIdentity(candidate.EntityKind, candidate.ExternalIds);
        var metadata = candidate.Metadata;
        var dates = (metadata?.Dates ?? new Dictionary<string, string>())
            .Select(pair => pair.Key.TryDecodeAs<EntityDateType>(out var type)
                ? new EntityMetadataDatePatch(type, pair.Value)
                : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
        var images = new List<ImageCandidate>();
        if (metadata?.PosterUrl is { } poster) {
            images.Add(new(MediaImageKind.Poster.ToCode(), poster, pluginId, null, null, null, null));
        }
        if (metadata?.BackdropUrl is { } backdrop) {
            images.Add(new(MediaImageKind.Backdrop.ToCode(), backdrop, pluginId, null, null, null, null));
        }
        var credits = (metadata?.Credits ?? [])
            .Select(credit => new CreditPatch(
                credit.Name,
                credit.Role.ToCode(),
                credit.Character,
                credit.SortOrder))
            .ToArray();
        var patch = new EntityMetadataPatch(
            candidate.Title,
            metadata?.Overview,
            candidate.ExternalIds,
            metadata?.Urls ?? [],
            metadata?.Tags ?? [],
            metadata?.Studio,
            credits,
            new Dictionary<string, string>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            metadata?.Classification) {
            AlternativeTitles = string.IsNullOrWhiteSpace(metadata?.OriginalTitle)
                || string.Equals(metadata.OriginalTitle, candidate.Title, StringComparison.Ordinal)
                    ? []
                    : [metadata.OriginalTitle],
            DateEntries = dates
        };
        return new(
            $"manager:{connectionId:D}:{rootIdentity.Namespace}:{rootIdentity.Value}",
            pluginId,
            candidate.EntityKind,
            Confidence: null,
            MatchReason,
            patch,
            images,
            Children: [],
            Candidates: [],
            Relationships: PersonRelationships(connectionId, pluginId, metadata?.Credits ?? []));
    }

    private static IReadOnlyList<EntityMetadataProposal> PersonRelationships(
        Guid connectionId,
        string pluginId,
        IReadOnlyList<ManagedPersonCredit> credits) {
        var relationships = new List<EntityMetadataProposal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var credit in credits) {
            var externalIds = credit.ExternalIds ?? new Dictionary<string, string>();
            var hasTmdbIdentity = TryCanonicalTmdbPersonId(externalIds, out var tmdb);
            var key = hasTmdbIdentity
                ? $"{ExternalIdProviders.Tmdb}:{tmdb}"
                : $"name:{credit.Name.Trim()}";
            if (!seen.Add(key)) {
                continue;
            }

            var urls = hasTmdbIdentity
                ? new[] { $"https://www.themoviedb.org/person/{tmdb}" }
                : [];
            var personImages = credit.ProfileUrl is { } profile
                ? new[] { new ImageCandidate(MediaImageKind.Profile.ToCode(), profile, pluginId, null, null, null, null) }
                : [];
            var personPatch = new EntityMetadataPatch(
                credit.Name,
                Description: null,
                externalIds,
                urls,
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int>(),
                Classification: null);
            relationships.Add(new(
                $"manager:{connectionId:D}:person:{relationships.Count}",
                pluginId,
                EntityKind.Person,
                Confidence: null,
                MatchReason,
                personPatch,
                personImages,
                Children: [],
                Candidates: [],
                Relationships: []));
        }
        return relationships;
    }

    private static bool TryCanonicalTmdbPersonId(
        IReadOnlyDictionary<string, string> values,
        out string tmdb) {
        if (values.TryGetValue(ExternalIdProviders.Tmdb, out var value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && id > 0
            && id.ToString(CultureInfo.InvariantCulture) == value) {
            tmdb = value;
            return true;
        }
        tmdb = string.Empty;
        return false;
    }

    private static ExternalIdentity CanonicalRootIdentity(
        EntityKind kind,
        IReadOnlyDictionary<string, string> externalIds) {
        if (kind == EntityKind.Movie
            && externalIds.TryGetValue(ExternalIdProviders.Tmdb, out var tmdb)
            && CanonicalNumericId(tmdb)) {
            return new(ExternalIdProviders.Tmdb, tmdb);
        }
        if (kind == EntityKind.VideoSeries) {
            if (externalIds.TryGetValue(ExternalIdProviders.Tmdb, out tmdb) && CanonicalNumericId(tmdb)) {
                return new(ExternalIdProviders.Tmdb, tmdb);
            }
            if (externalIds.TryGetValue(ExternalIdProviders.Tvdb, out var tvdb) && CanonicalNumericId(tvdb)) {
                return new(ExternalIdProviders.Tvdb, tvdb);
            }
        }
        throw new ArgumentException("The manager candidate has no canonical movie or series identity.");
    }

    private static bool CanonicalNumericId(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        && id > 0
        && id.ToString(CultureInfo.InvariantCulture) == value;
}
