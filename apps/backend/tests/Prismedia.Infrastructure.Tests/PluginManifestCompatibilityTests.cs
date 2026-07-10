using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class PluginManifestCompatibilityTests {
    [Fact]
    public void FiltersToDotnetPluginsCompatibleWithCurrentAppVersion() {
        var current = new Version(1, 0, 0);
        var candidates = new[]
        {
            Entry("tmdb", "0.9.0", ["v1"], "typescript", "0.1.0", null),
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", "1.0.0"),
            Entry("tmdb", "1.1.0", ["prismedia"], "dotnet-process", "1.0.0", null),
            Entry("other", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null)
        };

        var result = PluginCompatibilityResolver.LatestCompatible(candidates, "tmdb", current);

        Assert.NotNull(result);
        Assert.Equal("1.1.0", result.Version);
    }

    [Fact]
    public void ExistingProtocolOneManifestWithoutMaximumRemainsCompatible() {
        var compatibility = new PluginCompatibility("1.0.0", null, "1.0.0", null);

        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("3.0.0", null)]
    [InlineData("1.0.0", "1.9.9")]
    public void RejectsProtocolBoundsThatExcludeTheCurrentVersion(string pluginApiMin, string? pluginApiMax) {
        var compatibility = new PluginCompatibility(pluginApiMin, pluginApiMax, "1.0.0", null);

        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Fact]
    public void AcceptsProtocolRangeContainingTheCurrentVersion() {
        var compatibility = new PluginCompatibility(
            "1.0.0",
            PluginProtocol.CurrentSemanticVersion,
            "1.0.0",
            null);

        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.True(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("not-a-version", null)]
    [InlineData("1.0.0", "not-a-version")]
    public void RejectsMalformedProtocolBounds(string pluginApiMin, string? pluginApiMax) {
        var compatibility = new PluginCompatibility(pluginApiMin, pluginApiMax, "1.0.0", null);

        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility),
            new Version(1, 0, 0)));
        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility),
            new Version(1, 0, 0)));
    }

    [Fact]
    public void IntegerAndSemanticProtocolVersionsDescribeTheSameCurrentVersion() =>
        Assert.Equal(
            PluginProtocol.CurrentVersion,
            Version.Parse(PluginProtocol.CurrentSemanticVersion).Major);

    [Theory]
    [InlineData(PluginSearchFieldType.Text, "text")]
    [InlineData(PluginSearchFieldType.Number, "number")]
    [InlineData(PluginSearchFieldType.Year, "year")]
    public void SearchFieldTypesUseCanonicalCodeRegistryValues(PluginSearchFieldType type, string code) =>
        Assert.Equal(code, type.ToCode());

    [Fact]
    public void AcceptsManifestVersionTwoWithDistinctIdentityNamespacesAndSearchSchema() {
        var compatibility = new PluginCompatibility("1.0.0", null, "1.0.0", null);
        PluginEntitySupport[] supports =
        [
            new PluginEntitySupport(
                "video-series",
                [IdentifyAction.Search.ToCode(), IdentifyAction.LookupId.ToCode()],
                ["tmdb", "imdb"],
                new PluginSearchDefinition(
                [
                    new PluginSearchField("seriesTitle", "Series title", PluginSearchFieldType.Text, true, "Title", null),
                    new PluginSearchField("year", "Year", PluginSearchFieldType.Year, false, "2024", "Original premiere year")
                ]))
        ];
        var manifest = Manifest(
            compatibility,
            manifestVersion: 2,
            supports: supports);
        var entry = Entry(
            "tmdb-plugin",
            "2.0.0",
            ["prismedia"],
            "dotnet-process",
            "1.0.0",
            null,
            compatibility,
            manifestVersion: 2,
            supports: supports);

        Assert.True(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
        Assert.True(PluginCompatibilityResolver.IsCompatible(entry, new Version(1, 0, 0)));
    }

    [Fact]
    public void AcceptsSafeKindScopedIdentityUrlFormats() {
        var manifest = Manifest(
            new PluginCompatibility("2.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video-season",
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdbseason"],
                    IdentityUrls:
                    [
                        new PluginIdentityUrlFormat(
                            "tmdbseason",
                            "{seriesId}:{seasonNumber}",
                            "https://www.themoviedb.org/tv/{seriesId}/season/{seasonNumber}")
                    ])
            ]);

        Assert.True(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Fact]
    public void AcceptsIdentityUrlFormatThatUsesCapturedTokenMoreThanOnce() {
        var manifest = Manifest(
            new PluginCompatibility("2.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    [IdentifyAction.LookupId.ToCode()],
                    ["provider"],
                    IdentityUrls:
                    [
                        new PluginIdentityUrlFormat(
                            "provider",
                            "{id}",
                            "https://provider.example/items/{id}?selected={id}")
                    ])
            ]);

        Assert.True(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("imdb", "{id}", "https://www.imdb.com/title/{id}")]
    [InlineData("tmdb", "{id}{other}", "https://www.themoviedb.org/tv/{id}")]
    [InlineData("tmdb", "{seriesId}:{seasonNumber}", "https://www.themoviedb.org/tv/{seriesId}")]
    [InlineData("tmdb", "{id}", "https://user@www.themoviedb.org/tv/{id}")]
    [InlineData("tmdb", "{id}", "javascript:alert({id})")]
    [InlineData("tmdb", "{id}", "https://www.themoviedb.org/tv/{missing}")]
    public void RejectsUnsafeOrUnusableIdentityUrlFormats(
        string identityNamespace,
        string valuePattern,
        string urlTemplate) {
        var manifest = Manifest(
            new PluginCompatibility("2.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"],
                    IdentityUrls:
                    [
                        new PluginIdentityUrlFormat(identityNamespace, valuePattern, urlTemplate)
                    ])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Fact]
    public void RejectsDuplicateIdentityUrlFormatsForOneNamespace() {
        var format = new PluginIdentityUrlFormat("tmdb", "{id}", "https://www.themoviedb.org/tv/{id}");
        var manifest = Manifest(
            new PluginCompatibility("2.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video-series",
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"],
                    IdentityUrls: [format, format])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RejectsUnsupportedManifestSchemaVersions(int manifestVersion) {
        var compatibility = new PluginCompatibility("1.0.0", null, "1.0.0", null);

        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Entry("tmdb", "1.0.0", ["prismedia"], "dotnet-process", "1.0.0", null, compatibility, manifestVersion),
            new Version(1, 0, 0)));
        Assert.False(PluginCompatibilityResolver.IsCompatible(
            Manifest(compatibility, manifestVersion),
            new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("TMDB")]
    [InlineData(" tmdb")]
    [InlineData("tmdb/television")]
    public void VersionTwoRejectsNonCanonicalExternalIdentityNamespaces(string identityNamespace) {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    [IdentifyAction.LookupId.ToCode()],
                    [identityNamespace])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Fact]
    public void VersionTwoRejectsDuplicateExternalIdentityNamespaces() {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb", "tmdb"])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Fact]
    public void VersionTwoRequiresSearchSchemaWhenSearchActionIsDeclared() {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "book",
                    [IdentifyAction.Search.ToCode()],
                    ["openlibrary"])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Fact]
    public void VersionTwoRejectsEmptySupports() {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports: []);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("unknown-kind")]
    [InlineData("Video")]
    [InlineData(" video")]
    public void VersionTwoRejectsUnknownOrNonCanonicalEntityKinds(string entityKind) {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    entityKind,
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData()]
    [InlineData("cascade")]
    [InlineData("SEARCH")]
    [InlineData(" search")]
    public void VersionTwoRejectsMissingUnknownOrNonCanonicalIdentifyActions(params string[] actions) {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    actions,
                    ["tmdb"],
                    actions.Contains(IdentifyAction.Search.ToCode(), StringComparer.Ordinal)
                        ? new PluginSearchDefinition(
                        [
                            new PluginSearchField("title", "Title", PluginSearchFieldType.Text, true)
                        ])
                        : null)
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Fact]
    public void VersionTwoRejectsDuplicateIdentifyActionsAndEntityKindSupports() {
        var duplicateActions = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    [IdentifyAction.LookupId.ToCode(), IdentifyAction.LookupId.ToCode()],
                    ["tmdb"])
            ]);
        var duplicateSupports = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport("video", [IdentifyAction.LookupId.ToCode()], ["tmdb"]),
                new PluginEntitySupport("video", [IdentifyAction.LookupUrl.ToCode()], ["tmdb"])
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(duplicateActions, new Version(1, 0, 0)));
        Assert.False(PluginCompatibilityResolver.IsCompatible(duplicateSupports, new Version(1, 0, 0)));
    }

    [Fact]
    public void VersionTwoRejectsSearchSchemaWithoutSearchAction() {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "video",
                    [IdentifyAction.LookupId.ToCode()],
                    ["tmdb"],
                    new PluginSearchDefinition(
                    [
                        new PluginSearchField("title", "Title", PluginSearchFieldType.Text, true)
                    ]))
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("", "Title", "year")]
    [InlineData("title", "", "year")]
    [InlineData("title", "Title", "TITLE")]
    public void VersionTwoRejectsUnusableOrDuplicateSearchFields(
        string firstKey,
        string firstLabel,
        string secondKey) {
        var manifest = Manifest(
            new PluginCompatibility("1.0.0", null, "1.0.0", null),
            manifestVersion: 2,
            supports:
            [
                new PluginEntitySupport(
                    "book",
                    [IdentifyAction.Search.ToCode()],
                    ["openlibrary"],
                    new PluginSearchDefinition(
                    [
                        new PluginSearchField(firstKey, firstLabel, PluginSearchFieldType.Text, true),
                        new PluginSearchField(secondKey, "Year", PluginSearchFieldType.Year, false)
                    ]))
            ]);

        Assert.False(PluginCompatibilityResolver.IsCompatible(manifest, new Version(1, 0, 0)));
    }

    private static PluginIndexEntry Entry(
        string id,
        string version,
        string[] apiTags,
        string runtime,
        string appMin,
        string? appMax,
        PluginCompatibility? compatibility = null,
        int manifestVersion = 1,
        IReadOnlyList<PluginEntitySupport>? supports = null) =>
        new(
            Id: id,
            Name: id,
            Version: version,
            Date: "2026-05-16",
            Path: $"plugins/{id}/{id}.zip",
            Sha256: "abc",
            Runtime: runtime,
            IsNsfw: false,
            ManifestVersion: manifestVersion,
            ApiTags: apiTags,
            Compat: compatibility ?? new PluginCompatibility("1.0.0", null, appMin, appMax),
            Supports: supports ?? []);

    private static PluginManifest Manifest(
        PluginCompatibility compatibility,
        int manifestVersion = 1,
        IReadOnlyList<PluginEntitySupport>? supports = null) =>
        new(
            ManifestVersion: manifestVersion,
            ApiTags: ["prismedia"],
            Id: "tmdb",
            Name: "TMDB",
            Version: "1.0.0",
            Runtime: "dotnet-process",
            Entry: "tmdb.dll",
            Compat: compatibility,
            Auth: [],
            IsNsfw: false,
            Supports: supports ?? []);
}
