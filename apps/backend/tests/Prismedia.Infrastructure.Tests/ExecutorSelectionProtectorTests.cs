using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed class ExecutorSelectionProtectorTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), "prismedia-executor-selection-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void SelectionSurvivesHostRestartButCannotMoveToAnotherConnectionOrBeEdited() {
        var id = Guid.NewGuid();
        var inspection = new TransferInspection("selection", "revision", DateTimeOffset.UtcNow.AddMinutes(10),
            "https://source.test/publication?private-ticket=original", [new("item", "Book", EntityKind.Book)], false, []);
        var token = new ExecutorSelectionProtector(root).Protect(id, new("installation", 7, EntityKind.Book, inspection));
        Assert.DoesNotContain("private-ticket", token);
        var restarted = new ExecutorSelectionProtector(root);
        var selection = restarted.Read(id, token);
        Assert.Equal(7, selection.ConnectionRevision);
        Assert.Equal(inspection.CanonicalUrl, selection.Inspection.CanonicalUrl);
        Assert.Throws<ArgumentException>(() => restarted.Read(Guid.NewGuid(), token));
        Assert.Throws<ArgumentException>(() => restarted.Read(id, "invalid" + token));
        Assert.Throws<ArgumentException>(() => restarted.Protect(id, selection with { Inspection = inspection with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) } }));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
