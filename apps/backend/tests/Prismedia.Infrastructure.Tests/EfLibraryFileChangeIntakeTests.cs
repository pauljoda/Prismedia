using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Jobs;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfLibraryFileChangeIntakeTests {
    [Fact]
    public async Task RepeatedPathsCoalesceAndCutoffCompletionPreservesNewerObservation() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var rootId = Guid.NewGuid();
        const string scanKind = "scan-library";
        var path = Path.GetFullPath("/media/tv/Series/Season 01/Episode.mkv");
        var store = new EfLibraryFileChangeIntake(db);
        await store.RecordAsync(rootId, scanKind, [path, path], CancellationToken.None);
        var first = await store.LoadAsync(rootId, scanKind, 10, CancellationToken.None);
        Assert.Equal([path], first.Paths);

        await Task.Delay(5);
        await store.RecordAsync(rootId, scanKind, [path], CancellationToken.None);
        await store.CompleteAsync(
            rootId,
            scanKind,
            first.Paths,
            first.ObservedThrough,
            CancellationToken.None);

        Assert.True(await store.HasPendingAsync(rootId, scanKind, CancellationToken.None));
        var refreshed = await store.LoadAsync(rootId, scanKind, 10, CancellationToken.None);
        await store.CompleteAsync(
            rootId,
            scanKind,
            refreshed.Paths,
            refreshed.ObservedThrough,
            CancellationToken.None);
        Assert.False(await store.HasPendingAsync(rootId, scanKind, CancellationToken.None));
    }
}
