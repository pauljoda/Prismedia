using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedTrackingServiceTests {
    private static readonly ManagedTargetIdentity Owned = new("171", EntityKind.VideoEpisode, 1, 3, 3);
    private static readonly ManagedFileTarget Unowned = new("170", EntityKind.VideoEpisode, "Episode 2", 1, 2, 2);
    private static ManagedLibraryFile File(string id, params ManagedFileTarget[] targets) =>
        new(id, $"/series/{id}.mkv", 100, null, targets);

    [Fact]
    public void EstablishedScopeIgnoresSeparateUnownedFilesButKeepsSharedFileCoverageWhole() {
        var owned = new ManagedFileTarget("171", EntityKind.VideoEpisode, "Episode 3", 1, 3, 3);
        var remote = Snapshot([
            File("unowned", Unowned),
            File("owned", owned),
            File("shared", Unowned, owned)
        ]);

        var scoped = ManagedTrackingService.ScopeToEstablishedTargets(
            [new(Owned, Guid.NewGuid())], [], remote);

        Assert.Equal(["owned", "shared"], scoped.Files.Select(file => file.RemoteId).ToArray());
        Assert.Equal(["170", "171"], scoped.Files[1].Targets.Select(target => target.RemoteId).ToArray());
    }

    [Fact]
    public void EstablishedScopeKeepsCoordinateMatchedIdentityChangesForDomainReview() {
        var replacement = new ManagedFileTarget("999", EntityKind.VideoEpisode, "Episode 3", 1, 3, 3);

        var scoped = ManagedTrackingService.ScopeToEstablishedTargets(
            [new(Owned, Guid.NewGuid())], [], Snapshot([File("replacement", replacement)]));

        Assert.Equal("replacement", Assert.Single(scoped.Files).RemoteId);
    }

    [Fact]
    public void EstablishedScopeKeepsKnownPhysicalFileWhenProviderOmitsItsTargets() {
        var binding = new ManagedFileBinding(
            "owned-file", "/series/owned-file.mkv", 100, DateTimeOffset.UtcNow, true,
            [new(Owned, Guid.NewGuid(), Guid.NewGuid())]);

        var scoped = ManagedTrackingService.ScopeToEstablishedTargets(
            [new(Owned, Guid.NewGuid())], [binding], Snapshot([File("owned-file")]));

        Assert.Equal("owned-file", Assert.Single(scoped.Files).RemoteId);
    }

    private static ManagedItemSnapshot Snapshot(IReadOnlyList<ManagedLibraryFile> files) => new(
        new("series", EntityKind.VideoSeries, "Series", 2026,
            new Dictionary<string, string> { [ExternalIdProviders.Tvdb] = "42" }, false, "profile", 0),
        "/series/Series",
        files,
        DateTimeOffset.UtcNow);
}
