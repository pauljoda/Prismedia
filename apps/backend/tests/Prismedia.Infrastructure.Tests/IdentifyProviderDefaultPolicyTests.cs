using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyProviderDefaultPolicyTests {
    [Fact]
    public void ConfiguredProviderIsFirstForEveryKnownEntityKind() {
        var providers = EntityKindRegistry.All
            .Select(descriptor => Provider(
                $"provider-{descriptor.Value}",
                descriptor.GroupLabel,
                descriptor.Code))
            .Reverse()
            .ToArray();
        var settings = new IdentifyProviderSettings(
            EntityKindRegistry.All.ToDictionary(
                descriptor => descriptor.Code,
                descriptor => $"provider-{descriptor.Value}",
                StringComparer.Ordinal));

        foreach (var descriptor in EntityKindRegistry.All) {
            var ordered = IdentifyProviderDefaultPolicy.Order(providers, descriptor.Code, settings);

            Assert.Equal($"provider-{descriptor.Value}", ordered[0].Id);
        }
    }

    [Fact]
    public void MissingDisabledUnauthenticatedUninstalledAndIncompatibleDefaultsUseCatalogOrder() {
        var kind = EntityKindRegistry.Video.Code;
        var fallback = Provider("alpha", "Alpha", kind);
        var invalidDefaults = new[] {
            Provider("disabled", "Zulu", kind) with { Enabled = false },
            Provider("missing-auth", "Zulu", kind) with { MissingAuthKeys = ["token"] },
            Provider("uninstalled", "Zulu", kind) with { Installed = false },
            Provider("incompatible", "Zulu", EntityKindRegistry.Book.Code),
        };

        foreach (var invalid in invalidDefaults) {
            var ordered = IdentifyProviderDefaultPolicy.Order(
                [fallback, invalid],
                kind,
                new IdentifyProviderSettings(new Dictionary<string, string> {
                    [kind] = invalid.Id,
                }));

            Assert.Equal(fallback.Id, ordered[0].Id);
        }

        var unknown = IdentifyProviderDefaultPolicy.Order(
            [fallback],
            kind,
            new IdentifyProviderSettings(new Dictionary<string, string> {
                [kind] = "removed-provider",
            }));

        Assert.Equal(fallback.Id, unknown[0].Id);
    }

    [Fact]
    public void ProviderIdsMatchCaseInsensitivelyAndMovieUsesVideoCompatibility() {
        var provider = Provider("TMDB", "Zulu", EntityKindRegistry.Video.Code);
        var fallback = Provider("alpha", "Alpha", EntityKindRegistry.Movie.Code);
        var settings = new IdentifyProviderSettings(new Dictionary<string, string> {
            [EntityKindRegistry.Movie.Code] = "tmdb",
        });

        var ordered = IdentifyProviderDefaultPolicy.Order(
            [fallback, provider],
            EntityKindRegistry.Movie.Code,
            settings);

        Assert.Equal(provider.Id, ordered[0].Id);
    }

    [Fact]
    public void UnknownRequestedKindNeverAppliesAConfiguredDefault() {
        var providers = new[] {
            Provider("alpha", "Alpha", EntityKindRegistry.Video.Code),
            Provider("zulu", "Zulu", EntityKindRegistry.Video.Code),
        };
        var settings = new IdentifyProviderSettings(new Dictionary<string, string> {
            [EntityKindRegistry.Video.Code] = "zulu",
        });

        var ordered = IdentifyProviderDefaultPolicy.Order(providers, "unknown-kind", settings);

        Assert.Equal("alpha", ordered[0].Id);
    }

    private static PluginProvider Provider(string id, string name, string entityKind) =>
        new(
            id,
            name,
            "1.0.0",
            Installed: true,
            Enabled: true,
            IsNsfw: false,
            Supports: [new PluginEntitySupport(entityKind, [])],
            Auth: [],
            MissingAuthKeys: []);
}
