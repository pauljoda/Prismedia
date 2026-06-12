using Prismedia.Application.Jobs.Handlers;

namespace Prismedia.Infrastructure.Tests;

public sealed class AutoIdentifyConcurrencyGateTests {
    [Fact]
    public void WaitingInteractiveJobLocksBackgroundOutOfTheFreedSlot() {
        var gate = new AutoIdentifyConcurrencyGate();

        var background = gate.TryEnterBackground();
        Assert.NotNull(background);

        // The interactive job loses the race while background holds the slot, which marks it waiting.
        Assert.Null(gate.TryEnterInteractive());

        // Once the slot frees, background work is refused so the interactive retry wins it.
        background.Dispose();
        Assert.Null(gate.TryEnterBackground());

        var interactive = gate.TryEnterInteractive();
        Assert.NotNull(interactive);

        // After the interactive job runs and releases, background work flows again.
        interactive.Dispose();
        var resumed = gate.TryEnterBackground();
        Assert.NotNull(resumed);
        resumed.Dispose();
    }

    [Fact]
    public void InteractiveEntryIsExclusiveLikeAnyOtherLease() {
        var gate = new AutoIdentifyConcurrencyGate();

        var first = gate.TryEnterInteractive();
        Assert.NotNull(first);
        Assert.Null(gate.TryEnterInteractive());
        Assert.Null(gate.TryEnterBackground());

        first.Dispose();
        var second = gate.TryEnterInteractive();
        Assert.NotNull(second);
        second.Dispose();
    }
}
