using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Plugins;

/// <summary>
/// Compatibility bounds declared by a plugin artifact.
/// </summary>
/// <param name="PluginApiMin">Minimum plugin protocol version supported by the artifact.</param>
/// <param name="PluginApiMax">Optional maximum plugin protocol version supported by the artifact.</param>
/// <param name="PrismediaMin">Minimum Prismedia application version supported by the artifact.</param>
/// <param name="PrismediaMax">Optional maximum Prismedia application version supported by the artifact.</param>
public sealed record PluginCompatibility(
    string PluginApiMin,
    string? PluginApiMax,
    string PrismediaMin,
    string? PrismediaMax);

/// <summary>
/// Authentication field requested by a plugin.
/// </summary>
/// <param name="Key">Stable credential key passed to the plugin process.</param>
/// <param name="Label">Human-readable label shown in plugin settings.</param>
/// <param name="Required">Whether identify actions should be blocked when the credential is missing.</param>
/// <param name="Url">Optional upstream URL where users can create or manage the credential.</param>
public sealed record PluginAuthField(
    string Key,
    string Label,
    bool Required,
    string? Url);

/// <summary>
/// One schema-driven field a plugin accepts when searching an entity kind.
/// </summary>
/// <param name="Key">Stable plugin-owned key written to <see cref="IdentifyQuery.Fields"/>.</param>
/// <param name="Label">Human-readable field label.</param>
/// <param name="Type">Control and validation type.</param>
/// <param name="Required">Whether a search requires a non-empty value.</param>
/// <param name="Placeholder">Optional concise example shown inside the input.</param>
/// <param name="Help">Optional explanatory copy shown alongside the input.</param>
public sealed record PluginSearchField(
    string Key,
    string Label,
    PluginSearchFieldType Type,
    bool Required,
    string? Placeholder = null,
    string? Help = null);

/// <summary>Schema for the fields a plugin accepts when searching one entity kind.</summary>
/// <param name="Fields">Ordered search fields rendered and sent by Prismedia.</param>
public sealed record PluginSearchDefinition(IReadOnlyList<PluginSearchField> Fields);

/// <summary>
/// Declarative mapping from one plugin-owned external identity shape to its canonical web page.
/// </summary>
/// <param name="IdentityNamespace">External identity namespace handled by the containing support declaration.</param>
/// <param name="ValuePattern">
/// Whole-value pattern whose named tokens capture components of the opaque identity value.
/// </param>
/// <param name="UrlTemplate">
/// Absolute HTTP(S) URL template whose named tokens are percent-escaped before substitution.
/// </param>
public sealed record PluginIdentityUrlFormat(
    string IdentityNamespace,
    string ValuePattern,
    string UrlTemplate);

/// <summary>
/// Entity kind, identify actions, external identity namespaces, optional search schema, and identity links
/// supported by a plugin artifact.
/// </summary>
/// <param name="EntityKind">Stable Prismedia entity kind code.</param>
/// <param name="Actions">Canonical identify action codes such as lookup-id, lookup-url, and search.</param>
/// <param name="IdentityNamespaces">
/// External identity namespaces this support can resolve. These identify upstream records and are
/// intentionally independent from the plugin's own installation id.
/// </param>
/// <param name="Search">Plugin-defined search form when <paramref name="Actions"/> includes search.</param>
/// <param name="IdentityUrls">
/// Optional kind-scoped formats for linking declared identity namespaces to provider web pages.
/// </param>
public sealed record PluginEntitySupport(
    string EntityKind,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string>? IdentityNamespaces = null,
    PluginSearchDefinition? Search = null,
    IReadOnlyList<PluginIdentityUrlFormat>? IdentityUrls = null);

/// <summary>
/// Manifest embedded in a community plugin artifact.
/// </summary>
/// <param name="ManifestVersion">Manifest schema version; supports 1 and 2.</param>
/// <param name="ApiTags">Generation tags used by Prismedia to ignore older plugin systems.</param>
/// <param name="Id">Stable provider/plugin code such as tmdb.</param>
/// <param name="Name">Human-readable plugin name.</param>
/// <param name="Version">Plugin artifact semantic version.</param>
/// <param name="Runtime">Runtime code; supports dotnet-process.</param>
/// <param name="Entry">Entry assembly or executable path, relative to the manifest directory when not rooted.</param>
/// <param name="Compat">Compatibility bounds for plugin protocol and Prismedia versions.</param>
/// <param name="Auth">Credential fields requested by the plugin.</param>
/// <param name="IsNsfw">Whether this plugin should mark imported metadata as NSFW by default.</param>
/// <param name="Supports">Entity kind/action support declarations.</param>
public sealed record PluginManifest(
    int ManifestVersion,
    IReadOnlyList<string> ApiTags,
    string Id,
    string Name,
    string Version,
    string Runtime,
    string Entry,
    PluginCompatibility Compat,
    IReadOnlyList<PluginAuthField> Auth,
    bool IsNsfw,
    IReadOnlyList<PluginEntitySupport> Supports);

/// <summary>
/// Index entry consumed by the Prismedia plugin manager.
/// </summary>
/// <param name="Id">Stable plugin identifier.</param>
/// <param name="Name">Human-readable plugin name.</param>
/// <param name="Version">Plugin artifact semantic version.</param>
/// <param name="Date">Publication date string from the community index.</param>
/// <param name="Path">Relative artifact path in the plugin repository or index root.</param>
/// <param name="Sha256">SHA-256 checksum for packaged artifacts.</param>
/// <param name="Runtime">Runtime code; supports dotnet-process.</param>
/// <param name="IsNsfw">Whether this plugin can return NSFW metadata by default.</param>
/// <param name="ManifestVersion">Manifest schema version; supports 1 and 2.</param>
/// <param name="ApiTags">Tags used to gate plugin generations, including Prismedia.</param>
/// <param name="Compat">Declared compatibility bounds.</param>
/// <param name="Supports">Entity kind/action support declarations.</param>
public sealed record PluginIndexEntry(
    string Id,
    string Name,
    string Version,
    string Date,
    string Path,
    string Sha256,
    string Runtime,
    bool IsNsfw,
    int ManifestVersion,
    IReadOnlyList<string> ApiTags,
    PluginCompatibility Compat,
    IReadOnlyList<PluginEntitySupport> Supports);

/// <summary>
/// API-facing plugin provider summary.
/// </summary>
public sealed record PluginProvider(
    string Id,
    string Name,
    string Version,
    bool Installed,
    bool Enabled,
    bool IsNsfw,
    IReadOnlyList<PluginEntitySupport> Supports,
    IReadOnlyList<PluginAuthField> Auth,
    IReadOnlyList<string> MissingAuthKeys,
    bool UpdateAvailable = false,
    string? AvailableVersion = null);

/// <summary>
/// Request body for saving plugin credential values.
/// </summary>
public sealed record PluginAuthUpdateRequest(IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// A Stash community scraper available for installation from the CommunityScrapers index.
/// </summary>
/// <param name="ProviderId">Synthesized Prismedia provider id used to install the scraper.</param>
/// <param name="Name">Human-readable scraper name.</param>
/// <param name="Version">Index-reported version identifier.</param>
public sealed record StashScraperListing(string ProviderId, string Name, string Version);
