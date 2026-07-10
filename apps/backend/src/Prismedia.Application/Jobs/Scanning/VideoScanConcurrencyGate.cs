namespace Prismedia.Application.Jobs.Scanning;

/// <summary>
/// Serializes video library scans with TV file placement and immediate entity materialization.
/// A scan must never observe a newly moved episode before its acquisition hint is durable, otherwise
/// it can create a second series tree instead of binding the request-created wanted entities.
/// </summary>
public sealed class VideoScanConcurrencyGate {
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for exclusive access to the video filesystem/catalog reconciliation boundary.</summary>
    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken) {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable {
        private SemaphoreSlim? _semaphore = semaphore;

        public ValueTask DisposeAsync() {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
