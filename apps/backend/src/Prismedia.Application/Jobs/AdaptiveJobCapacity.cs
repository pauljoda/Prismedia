using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>Pure scheduling calculations for interactive lanes and CPU-weighted job resources.</summary>
public static class AdaptiveJobCapacity {
    /// <summary>Returns half the logical CPUs, rounded up and capped at four interactive lanes.</summary>
    public static int InteractiveLaneLimit(int logicalProcessorCount) {
        var processors = Math.Max(1, logicalProcessorCount);
        return Math.Clamp((processors + 1) / 2, 1, 4);
    }

    /// <summary>Leaves one logical processor outside the durable worker CPU budget when possible.</summary>
    public static int CpuPermitBudget(int logicalProcessorCount) =>
        Math.Max(1, Math.Max(1, logicalProcessorCount) - 1);

    /// <summary>Returns the permits required by one resource profile for the current server budget.</summary>
    public static int CpuCost(JobResourceClass resourceClass, int totalPermits) =>
        resourceClass switch {
            JobResourceClass.Light => 0,
            JobResourceClass.StandardCpu => 1,
            JobResourceClass.HeavyCpu => Math.Min(2, Math.Max(1, totalPermits)),
            _ => throw new ArgumentOutOfRangeException(nameof(resourceClass), resourceClass, null)
        };
}

/// <summary>Process-local weighted permit pool shared by interactive and background job execution.</summary>
internal sealed class JobCpuPermitPool(int totalPermits) {
    private readonly object _gate = new();
    private int _available = Math.Max(1, totalPermits);

    public IReadOnlyCollection<JobResourceClass> AcquirableClasses() {
        lock (_gate) {
            return Enum.GetValues<JobResourceClass>()
                .Where(resourceClass => AdaptiveJobCapacity.CpuCost(resourceClass, totalPermits) <= _available)
                .ToArray();
        }
    }

    public bool TryAcquire(JobResourceClass resourceClass, out IDisposable lease) {
        var cost = AdaptiveJobCapacity.CpuCost(resourceClass, totalPermits);
        lock (_gate) {
            if (cost > _available) {
                lease = EmptyLease.Instance;
                return false;
            }

            _available -= cost;
        }

        lease = cost == 0 ? EmptyLease.Instance : new PermitLease(this, cost);
        return true;
    }

    private void Release(int permits) {
        lock (_gate) {
            _available = Math.Min(Math.Max(1, totalPermits), _available + permits);
        }
    }

    private sealed class PermitLease(JobCpuPermitPool owner, int permits) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                owner.Release(permits);
            }
        }
    }

    private sealed class EmptyLease : IDisposable {
        internal static EmptyLease Instance { get; } = new();
        public void Dispose() { }
    }
}
