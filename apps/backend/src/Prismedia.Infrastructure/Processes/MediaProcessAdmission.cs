using Microsoft.Extensions.Logging;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Processes;

/// <summary>Samples whether the host currently has enough CPU and memory headroom for background media work.</summary>
public interface IHostLoadProbe {
    /// <summary>Returns whether a new background media process can safely start.</summary>
    ValueTask<bool> HasBackgroundHeadroomAsync(CancellationToken cancellationToken);
}

/// <summary>Coordinates process leases across the API and worker runtimes.</summary>
public interface IMediaProcessLeaseStore {
    /// <summary>
    /// Records an active playback process without waiting for or consuming background capacity.
    /// </summary>
    ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically acquires background capacity only when no playback lease exists and the shared cap permits it.
    /// </summary>
    ValueTask<IAsyncDisposable?> TryAcquireBackgroundAsync(
        int maxConcurrent,
        CancellationToken cancellationToken);
}

/// <summary>Admission boundary used by process execution to distinguish playback from derived-media work.</summary>
public interface IMediaProcessAdmission {
    /// <summary>Records playback without applying a capacity check.</summary>
    ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken);

    /// <summary>Waits until background media work has shared and measured host capacity.</summary>
    ValueTask<IAsyncDisposable> AcquireBackgroundAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Gives playback unconditional admission while allowing background ffmpeg work to start only when
/// the host and cross-process lease store both report headroom.
/// </summary>
public sealed class MediaProcessAdmission(
    IMediaProcessLeaseStore leases,
    IHostLoadProbe hostLoad,
    int maxBackgroundProcesses,
    TimeSpan? retryDelay = null,
    ILogger<MediaProcessAdmission>? logger = null) : IMediaProcessAdmission {
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);
    private readonly int _maxBackgroundProcesses = Math.Max(1, maxBackgroundProcesses);
    private readonly TimeSpan _retryDelay = retryDelay ?? DefaultRetryDelay;
    private readonly SemaphoreSlim _localBackgroundCapacity = new(Math.Max(1, maxBackgroundProcesses));

    /// <summary>
    /// Records playback immediately. This method never consults host load or background capacity.
    /// </summary>
    public async ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken) {
        try {
            return await leases.RegisterPlaybackAsync(cancellationToken);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            // Admission observability must never become a playback dependency. A missing playback
            // lease can temporarily reduce background coordination, but the stream still starts.
            logger?.LogWarning(ex,
                "Could not record playback media activity; playback will continue without an admission lease.");
            return EmptyAsyncLease.Instance;
        }
    }

    /// <summary>
    /// Waits cooperatively until measured host load and cross-process playback state permit a background process.
    /// </summary>
    public async ValueTask<IAsyncDisposable> AcquireBackgroundAsync(CancellationToken cancellationToken) {
        var loggedWait = false;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            if (await hostLoad.HasBackgroundHeadroomAsync(cancellationToken)) {
                await _localBackgroundCapacity.WaitAsync(cancellationToken);
                var localLeaseTransferred = false;
                try {
                    IAsyncDisposable? sharedLease;
                    try {
                        sharedLease = await leases.TryAcquireBackgroundAsync(
                            _maxBackgroundProcesses,
                            cancellationToken);
                    } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        throw;
                    } catch (Exception ex) {
                        // The worker is the only producer of background media processes. Retaining the
                        // local lease therefore preserves a safe cap even while shared storage recovers.
                        logger?.LogWarning(ex,
                            "Shared media admission is unavailable; using process-local background capacity.");
                        localLeaseTransferred = true;
                        return new LocalCapacityLease(_localBackgroundCapacity);
                    }

                    if (sharedLease is not null) {
                        if (loggedWait) {
                            logger?.LogInformation("Background media admission resumed after playback or host pressure cleared.");
                        }

                        localLeaseTransferred = true;
                        return new CompositeAsyncLease(
                            sharedLease,
                            new LocalCapacityLease(_localBackgroundCapacity));
                    }
                } finally {
                    // A returned composite or fallback lease owns this permit. Only release it here
                    // when shared capacity was unavailable because playback or another worker held it.
                    if (!localLeaseTransferred) {
                        _localBackgroundCapacity.Release();
                    }
                }
            }

            if (!loggedWait) {
                logger?.LogInformation(
                    "Background media admission is waiting for playback and measured host headroom.");
                loggedWait = true;
            }

            await Task.Delay(_retryDelay, cancellationToken);
        }
    }

    private sealed class LocalCapacityLease(SemaphoreSlim capacity) : IAsyncDisposable {
        private int _disposed;

        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                capacity.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompositeAsyncLease(
        IAsyncDisposable first,
        IAsyncDisposable second) : IAsyncDisposable {
        private int _disposed;

        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private sealed class EmptyAsyncLease : IAsyncDisposable {
        internal static EmptyAsyncLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
