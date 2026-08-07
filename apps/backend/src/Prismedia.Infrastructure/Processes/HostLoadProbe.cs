using System.Globalization;

namespace Prismedia.Infrastructure.Processes;

/// <summary>One host-level resource sample used to make a background admission decision.</summary>
/// <param name="TotalCpuTicks">Cumulative CPU ticks across all states and processors.</param>
/// <param name="IdleCpuTicks">Cumulative idle and I/O-wait CPU ticks.</param>
/// <param name="OneMinuteLoad">Host one-minute runnable-task load average.</param>
/// <param name="AvailableMemoryBytes">Memory currently available without swapping.</param>
/// <param name="TotalMemoryBytes">Total physical memory visible to the process.</param>
public sealed record HostLoadSample(
    long TotalCpuTicks,
    long IdleCpuTicks,
    double OneMinuteLoad,
    long AvailableMemoryBytes,
    long TotalMemoryBytes);

/// <summary>Reads host-level CPU, load, and memory counters.</summary>
public interface IHostLoadSampleReader {
    /// <summary>Returns a current sample, or <see langword="null"/> when the host does not expose one.</summary>
    ValueTask<HostLoadSample?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Measures host CPU across an interval and rejects new background media starts during sustained
/// CPU, run-queue, or memory pressure.
/// </summary>
public sealed class HostLoadProbe : IHostLoadProbe {
    private const double MaximumCpuUtilization = 0.70;
    private const double MaximumNormalizedLoad = 0.75;
    private const double MinimumAvailableMemoryRatio = 0.10;
    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromMilliseconds(250);
    private readonly IHostLoadSampleReader _reader;
    private readonly int _logicalProcessorCount;
    private readonly TimeSpan _sampleInterval;
    private readonly SemaphoreSlim _sampleGate = new(1, 1);
    private HostLoadSample? _previous;

    /// <summary>Creates a host-load probe using the platform reader and current processor count.</summary>
    public HostLoadProbe()
        : this(new ProcHostLoadSampleReader(), Environment.ProcessorCount) {
    }

    /// <summary>Creates a host-load probe with injectable samples for deterministic validation.</summary>
    public HostLoadProbe(
        IHostLoadSampleReader reader,
        int logicalProcessorCount,
        TimeSpan? sampleInterval = null) {
        _reader = reader;
        _logicalProcessorCount = Math.Max(1, logicalProcessorCount);
        _sampleInterval = sampleInterval ?? DefaultSampleInterval;
    }

    /// <inheritdoc />
    public async ValueTask<bool> HasBackgroundHeadroomAsync(CancellationToken cancellationToken) {
        await _sampleGate.WaitAsync(cancellationToken);
        try {
            var previous = _previous ?? await _reader.ReadAsync(cancellationToken);
            if (previous is null) {
                return true;
            }

            if (_previous is null && _sampleInterval > TimeSpan.Zero) {
                await Task.Delay(_sampleInterval, cancellationToken);
            }

            var current = await _reader.ReadAsync(cancellationToken);
            if (current is null) {
                _previous = previous;
                return true;
            }

            _previous = current;
            var totalDelta = current.TotalCpuTicks - previous.TotalCpuTicks;
            var idleDelta = current.IdleCpuTicks - previous.IdleCpuTicks;
            var cpuUtilization = totalDelta > 0
                ? 1d - Math.Clamp((double)idleDelta / totalDelta, 0d, 1d)
                : 0d;
            var normalizedLoad = current.OneMinuteLoad / _logicalProcessorCount;
            var availableMemoryRatio = current.TotalMemoryBytes > 0
                ? (double)current.AvailableMemoryBytes / current.TotalMemoryBytes
                : 1d;

            return cpuUtilization < MaximumCpuUtilization &&
                normalizedLoad < MaximumNormalizedLoad &&
                availableMemoryRatio >= MinimumAvailableMemoryRatio;
        } finally {
            _sampleGate.Release();
        }
    }
}

/// <summary>Reads Linux container-visible host counters from procfs.</summary>
public sealed class ProcHostLoadSampleReader : IHostLoadSampleReader {
    private const string CpuStatPath = "/proc/stat";
    private const string LoadAveragePath = "/proc/loadavg";
    private const string MemoryInfoPath = "/proc/meminfo";

    /// <inheritdoc />
    public async ValueTask<HostLoadSample?> ReadAsync(CancellationToken cancellationToken) {
        if (!File.Exists(CpuStatPath) || !File.Exists(LoadAveragePath) || !File.Exists(MemoryInfoPath)) {
            return null;
        }

        try {
            var statTask = File.ReadAllTextAsync(CpuStatPath, cancellationToken);
            var loadTask = File.ReadAllTextAsync(LoadAveragePath, cancellationToken);
            var memoryTask = File.ReadAllTextAsync(MemoryInfoPath, cancellationToken);
            await Task.WhenAll(statTask, loadTask, memoryTask);
            return Parse(await statTask, await loadTask, await memoryTask);
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        } catch (FormatException) {
            return null;
        }
    }

    internal static HostLoadSample Parse(string stat, string load, string memory) {
        var cpuLine = stat.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("cpu ", StringComparison.Ordinal));
        var cpuFields = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(value => long.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        var totalCpu = cpuFields.Sum();
        var idleCpu = cpuFields.ElementAtOrDefault(3) + cpuFields.ElementAtOrDefault(4);
        var oneMinuteLoad = double.Parse(
            load.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
            CultureInfo.InvariantCulture);

        var memoryValues = memory.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => long.Parse(
                    parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
                    CultureInfo.InvariantCulture) * 1024,
                StringComparer.Ordinal);

        return new HostLoadSample(
            totalCpu,
            idleCpu,
            oneMinuteLoad,
            memoryValues.GetValueOrDefault("MemAvailable"),
            memoryValues.GetValueOrDefault("MemTotal"));
    }
}
