using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class HostLoadProbeTests {
    [Fact]
    public async Task FirstDecisionMeasuresCpuAcrossTwoSamples() {
        var reader = new SequenceHostLoadSampleReader([
            new HostLoadSample(100, 50, 0.2, 900, 1_000),
            new HostLoadSample(200, 140, 0.2, 900, 1_000),
        ]);
        var probe = new HostLoadProbe(
            reader,
            logicalProcessorCount: 4,
            sampleInterval: TimeSpan.Zero);

        Assert.True(await probe.HasBackgroundHeadroomAsync(CancellationToken.None));
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task HighMeasuredCpuPreventsBackgroundAdmission() {
        var probe = new HostLoadProbe(
            new SequenceHostLoadSampleReader([
                new HostLoadSample(100, 50, 0.2, 900, 1_000),
                new HostLoadSample(200, 60, 0.2, 900, 1_000),
            ]),
            logicalProcessorCount: 4,
            sampleInterval: TimeSpan.Zero);

        Assert.False(await probe.HasBackgroundHeadroomAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HighNormalizedLoadOrMemoryPressurePreventsBackgroundAdmission() {
        var loadProbe = new HostLoadProbe(
            new SequenceHostLoadSampleReader([
                new HostLoadSample(100, 50, 3.2, 900, 1_000),
                new HostLoadSample(200, 140, 3.2, 900, 1_000),
            ]),
            logicalProcessorCount: 4,
            sampleInterval: TimeSpan.Zero);
        var memoryProbe = new HostLoadProbe(
            new SequenceHostLoadSampleReader([
                new HostLoadSample(100, 50, 0.2, 90, 1_000),
                new HostLoadSample(200, 140, 0.2, 90, 1_000),
            ]),
            logicalProcessorCount: 4,
            sampleInterval: TimeSpan.Zero);

        Assert.False(await loadProbe.HasBackgroundHeadroomAsync(CancellationToken.None));
        Assert.False(await memoryProbe.HasBackgroundHeadroomAsync(CancellationToken.None));
    }

    private sealed class SequenceHostLoadSampleReader(IReadOnlyList<HostLoadSample> samples)
        : IHostLoadSampleReader {
        private int _index;

        public int ReadCount => Volatile.Read(ref _index);

        public ValueTask<HostLoadSample?> ReadAsync(CancellationToken cancellationToken) {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, samples.Count - 1);
            return ValueTask.FromResult<HostLoadSample?>(samples[index]);
        }
    }
}
