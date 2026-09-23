using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed class ManagedComicRequestBindingTests {
    [Fact]
    public void ResolvedIssueRetainsItsExactLabelInTheDurableTarget() {
        var seriesId = Guid.NewGuid();
        var issueId = Guid.NewGuid();
        var seriesIdentity = new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-1" };
        var issueIdentity = new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2" };
        var requested = new ManagedLookupTarget(EntityKind.ComicInstallment, issueIdentity, IssueLabel: "½");
        var work = new ManagedLookupInput(EntityKind.ComicSeries, seriesIdentity, [requested]);
        var target = new ManagedRequestTarget(seriesId, "Series", work,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "root", "/comics", "/comics", "Comics"),
            [new(issueId, requested)]);
        var snapshot = new ManagedItemSnapshot(
            new("run", EntityKind.ComicSeries, "Series", 2024, seriesIdentity, false, null, 0),
            "/comics/series", [], DateTimeOffset.UtcNow);
        var resolved = new ManagedResolvedTarget("remote-issue", EntityKind.ComicInstallment, issueIdentity, IssueLabel: "½");

        var binding = Assert.Single(EfManagedRequestStore.ResolveBindings(target, snapshot, [resolved]));
        Assert.Equal(issueId, binding.EntityId);
        Assert.Equal("½", binding.Target.IssueLabel);
        Assert.Equal("remote-issue", binding.Target.RemoteTargetId);
        Assert.Throws<InvalidOperationException>(() => EfManagedRequestStore.ResolveBindings(target, snapshot,
            [resolved with { IssueLabel = "0.5" }]));
    }
}
