namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Process-wide concurrency gate for provider-backed identify work. One slot is shared by
/// background auto identify and user-triggered bulk identify, and the gate is priority-aware:
/// while an interactive job is waiting for the slot, background jobs are refused entry so the
/// user's job wins the slot as soon as the current holder releases it instead of racing a
/// large auto-identify backlog.
/// </summary>
public sealed class AutoIdentifyConcurrencyGate {
    /// <summary>
    /// How long a failed interactive entry keeps background work locked out. Interactive jobs
    /// retry within seconds, so this only needs to outlive one retry cycle; the expiry keeps a
    /// cancelled interactive job from blocking background identify forever.
    /// </summary>
    private static readonly TimeSpan InteractiveWaitWindow = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _interactiveWaitingUntilTicks;

    /// <summary>
    /// Attempts to reserve the identify slot for user-triggered (interactive) work without
    /// blocking. When the slot is busy, records that an interactive job is waiting so
    /// background work yields the slot on its next attempt.
    /// </summary>
    /// <returns>A lease when the slot was acquired; otherwise <see langword="null"/>.</returns>
    public IDisposable? TryEnterInteractive() {
        if (_semaphore.Wait(0)) {
            Interlocked.Exchange(ref _interactiveWaitingUntilTicks, 0);
            return new Lease(_semaphore);
        }

        Interlocked.Exchange(
            ref _interactiveWaitingUntilTicks,
            DateTimeOffset.UtcNow.Add(InteractiveWaitWindow).UtcTicks);
        return null;
    }

    /// <summary>
    /// Attempts to reserve the identify slot for background work without blocking. Refused while
    /// an interactive job is waiting for the slot, even if the slot itself is free, so the
    /// interactive job's retry always gets it first.
    /// </summary>
    /// <returns>A lease when the slot was acquired; otherwise <see langword="null"/>.</returns>
    public IDisposable? TryEnterBackground() {
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _interactiveWaitingUntilTicks)) {
            return null;
        }

        return _semaphore.Wait(0) ? new Lease(_semaphore) : null;
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                semaphore.Release();
            }
        }
    }
}
