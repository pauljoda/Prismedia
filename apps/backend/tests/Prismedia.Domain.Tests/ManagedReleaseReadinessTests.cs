using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class ManagedReleaseReadinessTests {
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    public void OnlyUnmonitoredAndIdleEvidencePermitsRelease(bool monitored, bool empty, bool idle, bool ready) {
        var evidence = new ManagedReleaseReadiness(monitored, empty, idle);
        Assert.Equal(ready, evidence.Problem is null);
    }
}
