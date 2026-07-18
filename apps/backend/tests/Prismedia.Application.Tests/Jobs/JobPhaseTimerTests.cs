using Prismedia.Application.Jobs;

namespace Prismedia.Application.Tests.Jobs;

public sealed class JobPhaseTimerTests {
    [Fact]
    public void TimingReportAggregatesRepeatedPhasesInFirstSeenOrder() {
        var report = new JobTimingReport(
            TimeSpan.FromSeconds(10),
            [
                new PhaseRecord("classify", TimeSpan.FromSeconds(1.25)),
                new PhaseRecord("persist", TimeSpan.FromSeconds(2)),
                new PhaseRecord("classify", TimeSpan.FromSeconds(0.75)),
            ]);

        Assert.Equal(
            "total=10.00s | classify=2.00s | persist=2.00s",
            report.ToLogString());
    }
}
