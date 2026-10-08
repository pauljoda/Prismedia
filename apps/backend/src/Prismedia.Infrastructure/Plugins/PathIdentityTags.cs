using System.Text.RegularExpressions;
using Prismedia.Contracts.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Reads provider IDs that media managers and servers embed in folder and file names, such as
/// Radarr's and Plex's <c>{tmdb-603}</c>, Jellyfin's <c>[tmdbid-603]</c>, Emby's
/// <c>[tmdbid=603]</c>, and the matching TheTVDB and IMDb forms.
/// </summary>
/// <remarks>
/// A namespace tagged with two different values across the supplied names is dropped rather than
/// guessed, and malformed values are ignored, so a stray or contradictory tag never becomes an
/// identity hint.
/// </remarks>
public static partial class PathIdentityTags {
    /// <summary>
    /// Parses the provider IDs tagged in a set of folder or file names.
    /// </summary>
    /// <param name="names">Folder or file names (not full paths) belonging to one entity.</param>
    /// <returns>One validated value per <see cref="ExternalIdProviders"/> key with no conflicting tag.</returns>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string?> names) {
        var found = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) {
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            foreach (Match match in TagRegex().Matches(name)) {
                var provider = match.Groups["provider"].Value.ToLowerInvariant();
                if (Normalize(provider, match.Groups["value"].Value) is not { } value) {
                    continue;
                }

                if (!found.TryGetValue(provider, out var values)) {
                    found[provider] = values = new HashSet<string>(StringComparer.Ordinal);
                }

                values.Add(value);
            }
        }

        return found
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? Normalize(string provider, string value) =>
        provider == ExternalIdProviders.Imdb
            ? ImdbValueRegex().IsMatch(value) ? value.ToLowerInvariant() : null
            : NumericValueRegex().IsMatch(value) ? value : null;

    // Braces and brackets must pair; the optional "id" suffix and "=" separator cover Jellyfin and
    // Emby naming. Provider keys come from ExternalIdProviders so the literals live in one place.
    [GeneratedRegex(
        $@"\{{(?<provider>{ExternalIdProviders.Tmdb}|{ExternalIdProviders.Tvdb}|{ExternalIdProviders.Imdb})(?:id)?[-=](?<value>[a-z0-9]+)\}}" +
        $@"|\[(?<provider>{ExternalIdProviders.Tmdb}|{ExternalIdProviders.Tvdb}|{ExternalIdProviders.Imdb})(?:id)?[-=](?<value>[a-z0-9]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericValueRegex();

    [GeneratedRegex(@"^tt[0-9]{7,10}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImdbValueRegex();
}
