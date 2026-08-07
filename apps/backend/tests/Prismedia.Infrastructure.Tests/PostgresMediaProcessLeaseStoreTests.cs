using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class PostgresMediaProcessLeaseStoreTests {
    [Fact]
    public async Task PlaybackIsUnlimitedWhileBackgroundAdmissionIsSharedAndExclusive() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await using var dataSource = NpgsqlDataSource.Create(context.Database.GetConnectionString()!);
        var store = new PostgresMediaProcessLeaseStore(
            dataSource,
            NullLogger<PostgresMediaProcessLeaseStore>.Instance);

        var background = await store.TryAcquireBackgroundAsync(1, CancellationToken.None);
        Assert.NotNull(background);
        Assert.Null(await store.TryAcquireBackgroundAsync(1, CancellationToken.None));

        var firstPlayback = await store.RegisterPlaybackAsync(CancellationToken.None);
        var secondPlayback = await store.RegisterPlaybackAsync(CancellationToken.None);

        await background!.DisposeAsync();
        Assert.Null(await store.TryAcquireBackgroundAsync(1, CancellationToken.None));

        await firstPlayback.DisposeAsync();
        Assert.Null(await store.TryAcquireBackgroundAsync(1, CancellationToken.None));

        await secondPlayback.DisposeAsync();
        await using var resumed = await store.TryAcquireBackgroundAsync(1, CancellationToken.None);
        Assert.NotNull(resumed);
    }
}
