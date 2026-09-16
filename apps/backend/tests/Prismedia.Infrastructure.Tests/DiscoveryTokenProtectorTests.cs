using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed class DiscoveryTokenProtectorTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"prismedia-discovery-tokens-{Guid.NewGuid():N}");

    [Fact]
    public void SelectionsSurviveRestartButCannotMoveBetweenConnectionsOrBeTamperedWith() {
        var connection = Guid.NewGuid();
        var selection = new SourceSelection("urn:isbn:1234", "https://catalog.test/feed", EntityKind.Book);
        var protector = new DiscoveryTokenProtector(_root);
        var token = protector.ProtectSelection(connection, selection);
        Assert.DoesNotContain(selection.Locator, token);
        Assert.Equal(selection, new DiscoveryTokenProtector(_root).ReadSelection(connection, token));
        Assert.Throws<ArgumentException>(() => protector.ReadSelection(Guid.NewGuid(), token));
        Assert.Throws<ArgumentException>(() => protector.ReadSelection(connection, token[..20] + "x" + token[21..]));
    }

    [Fact]
    public void ContinuationsAreBoundToQueryKindContainerAndPageSize() {
        var connection = Guid.NewGuid();
        var scope = new BrowseConnectionRequest(EntityKind.Book, Query: "first query", Container: "a");
        var protector = new DiscoveryTokenProtector(_root);
        var token = protector.ProtectCursor(connection, scope, "provider-page-2");
        Assert.Equal("provider-page-2", protector.ReadCursor(connection, scope with { Cursor = token }, token));
        foreach (var changed in new[] { scope with { Query = "other" }, scope with { EntityKind = EntityKind.ComicInstallment },
            scope with { Container = "b" }, scope with { Limit = 100 } })
            Assert.Throws<ArgumentException>(() => protector.ReadCursor(connection, changed, token));
        Assert.Throws<ArgumentException>(() => protector.ReadSelection(connection, token));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
