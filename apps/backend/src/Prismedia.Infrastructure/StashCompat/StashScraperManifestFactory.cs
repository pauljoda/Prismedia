using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Synthesizes a Prismedia <see cref="PluginManifest"/> for an installed Stash community
/// scraper. Stash scrapers ship no Prismedia manifest, so the id, name, supported kinds, and
/// NSFW default are derived from the YAML definition and its file name.
/// </summary>
public static class StashScraperManifestFactory {
    /// <summary>
    /// Maps each Stash capability to the Prismedia entity kind and identify action it supports.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Kind, string Action)> CapabilityMap =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase) {
            [StashScraperDefinition.SceneByUrl] = (EntityKind.Video.ToCode(), IdentifyAction.LookupUrl.ToCode()),
            [StashScraperDefinition.SceneByName] = (EntityKind.Video.ToCode(), IdentifyAction.Search.ToCode()),
            [StashScraperDefinition.SceneByFragment] = (EntityKind.Video.ToCode(), IdentifyAction.Search.ToCode()),
            [StashScraperDefinition.SceneByQueryFragment] = (EntityKind.Video.ToCode(), IdentifyAction.Search.ToCode()),
            [StashScraperDefinition.MovieByUrl] = (EntityKind.Movie.ToCode(), IdentifyAction.LookupUrl.ToCode()),
            [StashScraperDefinition.PerformerByUrl] = (EntityKind.Person.ToCode(), IdentifyAction.LookupUrl.ToCode()),
            [StashScraperDefinition.PerformerByName] = (EntityKind.Person.ToCode(), IdentifyAction.Search.ToCode()),
            [StashScraperDefinition.StudioByUrl] = (EntityKind.Studio.ToCode(), IdentifyAction.LookupUrl.ToCode()),
            [StashScraperDefinition.StudioByName] = (EntityKind.Studio.ToCode(), IdentifyAction.Search.ToCode()),
            [StashScraperDefinition.GalleryByUrl] = (EntityKind.Gallery.ToCode(), IdentifyAction.LookupUrl.ToCode()),
            [StashScraperDefinition.TagByUrl] = (EntityKind.Tag.ToCode(), IdentifyAction.LookupUrl.ToCode()),
            [StashScraperDefinition.TagByName] = (EntityKind.Tag.ToCode(), IdentifyAction.Search.ToCode())
        };

    /// <summary>
    /// Builds a synthesized manifest for a scraper YAML file, or null when the YAML is invalid
    /// or declares no mappable capability.
    /// </summary>
    /// <param name="yaml">Raw scraper YAML.</param>
    /// <param name="yamlPath">Absolute path to the scraper file (used to derive a stable id).</param>
    /// <returns>The synthesized manifest, or null.</returns>
    public static PluginManifest? TryCreate(string yaml, string yamlPath) {
        var definition = StashScraperDefinition.TryParse(yaml);
        if (definition is null) {
            return null;
        }

        var supports = BuildSupports(definition);
        if (supports.Count == 0) {
            return null;
        }

        var id = BuildId(yamlPath);
        return new PluginManifest(
            ManifestVersion: 1,
            ApiTags: ["prismedia"],
            Id: id,
            Name: definition.Name,
            Version: "1.0.0",
            Runtime: StashCompatRunner.Code,
            Entry: yamlPath,
            Compat: new PluginCompatibility("1.0.0", null, "1.0.0", null),
            Auth: [],
            IsNsfw: true,
            Supports: supports);
    }

    private static IReadOnlyList<PluginEntitySupport> BuildSupports(StashScraperDefinition definition) {
        var byKind = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in definition.Capabilities()) {
            if (!CapabilityMap.TryGetValue(capability, out var mapping)) {
                continue;
            }

            if (!byKind.TryGetValue(mapping.Kind, out var actions)) {
                actions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                byKind[mapping.Kind] = actions;
            }

            actions.Add(mapping.Action);
        }

        return byKind
            .Select(pair => new PluginEntitySupport(pair.Key, pair.Value.ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Builds a stable, collision-resistant provider id from the scraper file name,
    /// prefixed with <c>stash-</c> so it is distinguishable from dotnet plugin ids.
    /// </summary>
    private static string BuildId(string yamlPath) =>
        ProviderIdFor(Path.GetFileNameWithoutExtension(yamlPath));

    /// <summary>
    /// Builds the synthesized provider id (<c>stash-&lt;slug&gt;</c>) for a scraper name or file stem.
    /// Used both for discovery (from a file name) and install (from a CommunityScrapers index id)
    /// so the two paths agree on the provider id.
    /// </summary>
    /// <param name="name">Scraper file stem or index id.</param>
    /// <returns>The provider id.</returns>
    public static string ProviderIdFor(string name) {
        var slug = new string(name
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray())
            .Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrEmpty(slug) ? "stash-scraper" : $"stash-{slug}";
    }
}
