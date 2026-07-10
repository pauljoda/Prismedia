using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers the registry-driven request layer: selected-plugin schema search routing and registry shape
/// invariants shared by Discover, proposal review, and acquisition.
/// </summary>
public sealed class RequestServicesTests {
    [Fact]
    public async Task PluginSearchRoutesOneDiscoverableKindAndExactPluginFields() {
        var source = new FakePluginSearchSource();
        var service = new RequestPluginSearchService(source);
        var fields = new Dictionary<string, string> {
            ["seriesTitle"] = "The Expanse: Origins",
            ["year"] = "2015"
        };

        var response = await service.SearchAsync(
            new RequestPluginSearchRequest(RequestMediaKind.Series, "zeta-metadata", fields),
            hideNsfw: true,
            CancellationToken.None);

        Assert.Empty(response.ProviderErrors);
        Assert.Equal(RequestMediaKind.Series, source.LastDescriptor?.Kind);
        Assert.Equal("zeta-metadata", source.LastPluginId);
        Assert.Same(fields, source.LastFields);
        Assert.True(source.LastHideNsfw);
    }

    [Fact]
    public async Task PluginSearchPropagatesSchemaValidationFailures() {
        var source = new FakePluginSearchSource {
            Failure = new RequestSearchValidationException("The required title field is missing.")
        };
        var service = new RequestPluginSearchService(source);

        var error = await Assert.ThrowsAsync<RequestSearchValidationException>(() => service.SearchAsync(
            new RequestPluginSearchRequest(RequestMediaKind.Movie, "cinema-metadata", new Dictionary<string, string>()),
            hideNsfw: false,
            CancellationToken.None));

        Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistryShapeInvariantsHold() {
        foreach (var descriptor in RequestKindRegistry.All) {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Label));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Plural));
            Assert.Equal(descriptor.ChildKind is not null, descriptor.ChildNoun is not null);
            if (descriptor.Committable) {
                Assert.NotNull(descriptor.ProfileEntityKind);
                Assert.NotNull(descriptor.LibraryRootMediaCapability);
            }

            // A committable container must fan out into a committable child kind, or a commit could
            // never start an acquisition for it.
            if (descriptor is { IsContainer: true, Committable: true }) {
                var child = RequestKindRegistry.ChildOf(descriptor);
                Assert.NotNull(child);
                Assert.True(child!.Committable, $"{descriptor.Kind} fans out into non-committable {child.Kind}");
                Assert.False(child.IsContainer, $"{descriptor.Kind}'s child {child.Kind} must be a leaf");
            }

            if (descriptor.MaterializeChildPhantoms) {
                var child = RequestKindRegistry.ChildOf(descriptor);
                Assert.NotNull(child);
                Assert.True(child!.Committable, $"{descriptor.Kind}'s structural child must be requestable");
                Assert.Same(
                    descriptor,
                    RequestKindRegistry.FindChildMaterializingUnit(descriptor.WantedEntityKind));
            }
        }

        // The registry is the closed set for the flow; kinds must be unique.
        Assert.Equal(RequestKindRegistry.All.Count, RequestKindRegistry.All.Select(d => d.Kind).Distinct().Count());
    }

    private sealed class FakePluginSearchSource : IPluginRequestSearchSource {
        public RequestKindDescriptor? LastDescriptor { get; private set; }
        public string? LastPluginId { get; private set; }
        public IReadOnlyDictionary<string, string>? LastFields { get; private set; }
        public bool LastHideNsfw { get; private set; }
        public Exception? Failure { get; init; }

        public Task<IReadOnlyList<RequestSearchResult>> SearchAsync(
            RequestKindDescriptor descriptor,
            string pluginId,
            IReadOnlyDictionary<string, string> fields,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            LastDescriptor = descriptor;
            LastPluginId = pluginId;
            LastFields = fields;
            LastHideNsfw = hideNsfw;
            return Failure is null
                ? Task.FromResult<IReadOnlyList<RequestSearchResult>>([])
                : Task.FromException<IReadOnlyList<RequestSearchResult>>(Failure);
        }
    }
}
