using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class ManagedControlOperationTests {
    private static ManagedControlOperation Create(bool configure = true, bool search = true) =>
        ManagedControlOperation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), configure, search);
    private static readonly ManagedCommandIdentity Command = new("42", DateTimeOffset.Parse("2026-09-16T20:00:00.1234567Z"));

    [Fact] public void Lost_configuration_can_be_observed_but_never_redispatched() {
        var action = Create(); action.BeginConfiguration(); action.RequireReview();
        Assert.Throws<InvalidOperationException>(action.BeginConfiguration);
        Assert.False(action.CanCancel);
        action.ConfirmConfiguration(); Assert.True(action.State.ConfigurationConfirmed);
        Assert.Equal(ManagedControlPhase.PendingSearch, action.State.Phase);
        Assert.False(action.State.ReviewRequired);
    }
    [Fact] public void Lost_search_keeps_ownership_and_cannot_be_repeated() {
        var action = Create(false); action.BeginSearch(); action.RequireReview();
        Assert.True(action.IsActive); Assert.False(action.CanCancel);
        Assert.Throws<InvalidOperationException>(action.BeginSearch);
        Assert.Throws<InvalidOperationException>(action.Cancel);
        action.CloseUnverified(); Assert.False(action.IsActive);
        Assert.Equal(ManagedControlPhase.ClosedUnverified, action.State.Phase);
    }
    [Fact] public void Closing_requires_presented_uncertainty() {
        var action = Create(false); action.BeginSearch();
        Assert.Throws<InvalidOperationException>(action.CloseUnverified);
    }
    [Fact] public void Cancelling_unsent_search_preserves_confirmed_settings() {
        var action = Create(); action.ConfirmConfiguration(); action.Cancel();
        Assert.True(action.State.ConfigurationConfirmed); Assert.False(action.IsActive);
        Assert.Throws<InvalidOperationException>(action.BeginSearch);
    }
    [Fact] public void Rejection_of_search_does_not_revert_settings() {
        var action = Create(); action.ConfirmConfiguration(); action.BeginSearch(); action.Reject();
        Assert.True(action.State.ConfigurationConfirmed); Assert.False(action.IsActive);
    }
    [Fact] public void Command_identity_includes_full_queue_timestamp() {
        var action = Create(false); action.BeginSearch(); action.AcceptCommand(Command);
        Assert.Throws<InvalidOperationException>(() => action.ObserveCommand(Command with { QueuedAt = Command.QueuedAt.AddTicks(1) }, ManagedCommandStatus.Completed));
    }
    [Fact] public void Unknown_history_can_recover_without_redispatch() {
        var action = Create(false); action.BeginSearch(); action.AcceptCommand(Command);
        action.ObserveCommand(Command, ManagedCommandStatus.Unknown);
        Assert.True(action.IsActive); Assert.True(action.CanCloseUnverified);
        action.ObserveCommand(Command, ManagedCommandStatus.Running);
        Assert.False(action.State.ReviewRequired);
        action.ObserveCommand(Command, ManagedCommandStatus.Completed);
        Assert.False(action.IsActive);
        Assert.Throws<InvalidOperationException>(() => action.ObserveCommand(Command, ManagedCommandStatus.Unknown));
    }
    [Theory]
    [InlineData(ManagedCommandStatus.Failed, ManagedControlPhase.Failed)]
    [InlineData(ManagedCommandStatus.Cancelled, ManagedControlPhase.Cancelled)]
    public void Terminal_execution_is_retained(ManagedCommandStatus status, ManagedControlPhase expected) {
        var action = Create(false); action.BeginSearch(); action.AcceptCommand(Command); action.ObserveCommand(Command, status);
        Assert.Equal(expected, action.State.Phase); Assert.False(action.IsActive);
    }
}
