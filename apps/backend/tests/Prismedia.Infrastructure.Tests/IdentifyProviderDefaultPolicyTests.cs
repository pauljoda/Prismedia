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
                $"provider-{descriptor.Kind}",
                descriptor.GroupLabel,
                descriptor.Code))
            .Reverse()
            .ToArray();
        var settings = new IdentifyProviderSettings(
            EntityKindRegistry.All.ToDictionary(
                descriptor => descriptor.Code,
                descriptor => $"provider-{descriptor.Kind}",
                StringComparer.Ordinal));

        foreach (var descriptor in EntityKindRegistry.All) {
            var ordered = IdentifyProviderDefaultPolicy.Order(providers, descriptor.Code, settings);

            Assert.Equal($"provider-{descriptor.Kind}", ordered[0].Id);
        }
    }

    [Fact]
    public void MissingDisabledUnauthenticatedUninstalledAndIncompatibleDefaultsUseCatalogOrder() {
        var kind = EntityKind.Video.ToCode();
        var fallback = Provider("alpha", "Alpha", kind);
        var invalidDefaults = new[] {
            Provider("disabled", "Zulu", kind) with { Enabled = false },
            Provider("missing-auth", "Zulu", kind) with { MissingAuthKeys = ["token"] },
            Provider("uninstalled", "Zulu", kind) with { Installed = false },
            Provider("incompatible", "Zulu", EntityKind.Book.ToCode()),
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
        var provider = Provider("TMDB", "Zulu", EntityKind.Video.ToCode());
        var fallback = Provider("alpha", "Alpha", EntityKind.Movie.ToCode());
        var settings = new IdentifyProviderSettings(new Dictionary<string, string> {
            [EntityKind.Movie.ToCode()] = "tmdb",
        });

        var ordered = IdentifyProviderDefaultPolicy.Order(
            [fallback, provider],
            EntityKind.Movie.ToCode(),
            settings);

        Assert.Equal(provider.Id, ordered[0].Id);
    }

    [Fact]
    public void UnknownRequestedKindNeverAppliesAConfiguredDefault() {
        var providers = new[] {
            Provider("alpha", "Alpha", EntityKind.Video.ToCode()),
            Provider("zulu", "Zulu", EntityKind.Video.ToCode()),
        };
        var settings = new IdentifyProviderSettings(new Dictionary<string, string> {
            [EntityKind.Video.ToCode()] = "zulu",
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
