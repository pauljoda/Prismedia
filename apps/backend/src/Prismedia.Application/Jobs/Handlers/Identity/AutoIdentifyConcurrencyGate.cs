namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Process-wide concurrency gate for provider-backed auto identify work.
/// </summary>
public sealed class AutoIdentifyConcurrencyGate {
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Waits until no other auto identify job is running in this worker process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A lease that releases the gate when disposed.</returns>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken) {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    /// <summary>
    /// Attempts to reserve the auto identify slot without blocking a worker thread.
    /// </summary>
    /// <returns>A lease when the slot was acquired; otherwise <see langword="null"/>.</returns>
    public IDisposable? TryEnter() => _semaphore.Wait(0) ? new Lease(_semaphore) : null;

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                semaphore.Release();
            }
        }
    }
}
