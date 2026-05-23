using System.Diagnostics;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Tracks per-phase elapsed times within a job for structured performance reporting.
/// Create one at the start of a handler, call <see cref="Phase"/> around each logical step,
/// then <see cref="Finish"/> to produce a summary.
/// </summary>
public sealed class JobPhaseTimer {
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly List<PhaseRecord> _phases = [];

    /// <summary>
    /// Times a named phase. Dispose the returned handle when the phase ends.
    /// Phases may nest or overlap — each is an independent stopwatch.
    /// </summary>
    public PhaseScope Phase(string name) => new(this, name);

    /// <summary>
    /// Stops the overall timer and returns a summary of all recorded phases.
    /// </summary>
    public JobTimingReport Finish() {
        _total.Stop();
        return new JobTimingReport(_total.Elapsed, _phases.ToList());
    }

    private void Record(string name, TimeSpan elapsed) =>
        _phases.Add(new PhaseRecord(name, elapsed));

    public sealed class PhaseScope(JobPhaseTimer timer, string name) : IDisposable {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private bool _disposed;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _sw.Stop();
            timer.Record(name, _sw.Elapsed);
        }
    }
}

public sealed record PhaseRecord(string Name, TimeSpan Duration);

public sealed record JobTimingReport(TimeSpan Total, IReadOnlyList<PhaseRecord> Phases) {
    /// <summary>
    /// Formats the report as a compact single-line summary for structured logging.
    /// Example: "total=12.34s | discover=0.12s | upsert=1.45s | enqueue=0.89s"
    /// </summary>
    public string ToLogString() {
        var parts = new List<string> { $"total={Total.TotalSeconds:F2}s" };
        foreach (var phase in Phases) {
            parts.Add($"{phase.Name}={phase.Duration.TotalSeconds:F2}s");
        }
        return string.Join(" | ", parts);
    }
}
