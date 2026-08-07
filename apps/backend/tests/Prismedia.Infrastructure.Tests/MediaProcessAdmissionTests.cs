using System.Collections.Concurrent;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class MediaProcessAdmissionTests {
    [Fact]
    public async Task PlaybackRegistrationNeverWaitsForBackgroundCapacity() {
        var store = new InMemoryMediaProcessLeaseStore();
        var admission = new MediaProcessAdmission(
            store,
            new MutableHostLoadProbe { HasBackgroundHeadroom = true },
            maxBackgroundProcesses: 1,
            retryDelay: TimeSpan.FromMilliseconds(10));

        await using var background = await admission.AcquireBackgroundAsync(CancellationToken.None);

        var firstPlayback = admission.RegisterPlaybackAsync(CancellationToken.None).AsTask();
        var secondPlayback = admission.RegisterPlaybackAsync(CancellationToken.None).AsTask();

        await Task.WhenAll(firstPlayback, secondPlayback).WaitAsync(TimeSpan.FromSeconds(1));
        await using var first = await firstPlayback;
        await using var second = await secondPlayback;
        Assert.Equal(2, store.ActivePlaybackCount);
    }

    [Fact]
    public async Task BackgroundWaitsUntilAllPlaybackProcessesFinish() {
        var store = new InMemoryMediaProcessLeaseStore();
        var admission = new MediaProcessAdmission(
            store,
            new MutableHostLoadProbe { HasBackgroundHeadroom = true },
            maxBackgroundProcesses: 2,
            retryDelay: TimeSpan.FromMilliseconds(10));

        var first = await admission.RegisterPlaybackAsync(CancellationToken.None);
        var second = await admission.RegisterPlaybackAsync(CancellationToken.None);
        var background = admission.AcquireBackgroundAsync(CancellationToken.None).AsTask();

        await Task.Delay(40);
        Assert.False(background.IsCompleted);

        await first.DisposeAsync();
        await Task.Delay(40);
        Assert.False(background.IsCompleted);

        await second.DisposeAsync();
        await using var lease = await background.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, store.ActiveBackgroundCount);
    }

    [Fact]
    public async Task BackgroundWaitsForMeasuredHostHeadroom() {
        var store = new InMemoryMediaProcessLeaseStore();
        var load = new MutableHostLoadProbe { HasBackgroundHeadroom = false };
        var admission = new MediaProcessAdmission(
            store,
            load,
            maxBackgroundProcesses: 1,
            retryDelay: TimeSpan.FromMilliseconds(10));

        var background = admission.AcquireBackgroundAsync(CancellationToken.None).AsTask();
        await Task.Delay(40);
        Assert.False(background.IsCompleted);

        load.HasBackgroundHeadroom = true;
        await using var lease = await background.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(load.SampleCount >= 2);
    }

    [Fact]
    public async Task BackgroundConcurrencyIsBoundedIndependentlyOfPlayback() {
        var store = new InMemoryMediaProcessLeaseStore();
        var admission = new MediaProcessAdmission(
            store,
            new MutableHostLoadProbe { HasBackgroundHeadroom = true },
            maxBackgroundProcesses: 1,
            retryDelay: TimeSpan.FromMilliseconds(10));

        var first = await admission.AcquireBackgroundAsync(CancellationToken.None);
        var second = admission.AcquireBackgroundAsync(CancellationToken.None).AsTask();
        await Task.Delay(40);
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();
        await using var next = await second.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, store.ActiveBackgroundCount);
    }

    [Fact]
    public async Task PlaybackStillProceedsWhenSharedAdmissionStorageIsUnavailable() {
        var admission = new MediaProcessAdmission(
            new FailingMediaProcessLeaseStore(),
            new MutableHostLoadProbe { HasBackgroundHeadroom = false },
            maxBackgroundProcesses: 1);

        await using var playback = await admission.RegisterPlaybackAsync(CancellationToken.None);
    }

    private sealed class MutableHostLoadProbe : IHostLoadProbe {
        private int _sampleCount;

        public bool HasBackgroundHeadroom { get; set; }
        public int SampleCount => Volatile.Read(ref _sampleCount);

        public ValueTask<bool> HasBackgroundHeadroomAsync(CancellationToken cancellationToken) {
            Interlocked.Increment(ref _sampleCount);
            return ValueTask.FromResult(HasBackgroundHeadroom);
        }
    }

    private sealed class InMemoryMediaProcessLeaseStore : IMediaProcessLeaseStore {
        private readonly ConcurrentDictionary<Guid, MediaProcessKind> _leases = new();

        public int ActivePlaybackCount => _leases.Values.Count(value => value == MediaProcessKind.Playback);
        public int ActiveBackgroundCount => _leases.Values.Count(value => value == MediaProcessKind.Background);

        public ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable>(Add(MediaProcessKind.Playback));

        public ValueTask<IAsyncDisposable?> TryAcquireBackgroundAsync(
            int maxConcurrent,
            CancellationToken cancellationToken) {
            lock (_leases) {
                if (ActivePlaybackCount > 0 || ActiveBackgroundCount >= maxConcurrent) {
                    return ValueTask.FromResult<IAsyncDisposable?>(null);
                }

                return ValueTask.FromResult<IAsyncDisposable?>(Add(MediaProcessKind.Background));
            }
        }

        private IAsyncDisposable Add(MediaProcessKind kind) {
            var id = Guid.NewGuid();
            _leases[id] = kind;
            return new AsyncActionLease(() => _leases.TryRemove(id, out _));
        }
    }

    private sealed class FailingMediaProcessLeaseStore : IMediaProcessLeaseStore {
        public ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<IAsyncDisposable>(new InvalidOperationException("storage unavailable"));

        public ValueTask<IAsyncDisposable?> TryAcquireBackgroundAsync(
            int maxConcurrent,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<IAsyncDisposable?>(new InvalidOperationException("storage unavailable"));
    }

    private sealed class AsyncActionLease(Action release) : IAsyncDisposable {
        private int _disposed;

        public ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
