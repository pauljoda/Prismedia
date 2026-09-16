using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Exact command identity. Retain its full timestamp precision when persisting this record.</summary>
public sealed record ManagedCommandIdentity(string Id, DateTimeOffset QueuedAt);

/// <summary>Rehydratable intent progress; remote effects are fenced by a saved uncertain phase.</summary>
public sealed record ManagedControlOperationState(Guid OperationId, Guid ConnectionId, Guid HoldingId, long Revision,
    bool HasConfiguration, bool SearchRequested, ManagedControlPhase Phase, bool ConfigurationConfirmed = false,
    ManagedCommandIdentity? Command = null, ManagedCommandStatus? CommandStatus = null, bool ReviewRequired = false);

/// <summary>Prevents implicit repeat mutations when the upstream manager offers no idempotency guarantee.</summary>
public sealed class ManagedControlOperation(ManagedControlOperationState state) {
    /// <summary>Immutable progress saved with optimistic concurrency at every external boundary.</summary>
    public ManagedControlOperationState State { get; private set; } = state;
    /// <summary>Whether this action still holds its exclusive control slot. This is separate from fulfillment ownership.</summary>
    public bool IsActive => State.Phase is ManagedControlPhase.PendingConfiguration or ManagedControlPhase.ConfigurationUncertain
        or ManagedControlPhase.PendingSearch or ManagedControlPhase.SearchUncertain or ManagedControlPhase.AwaitingCommand;
    /// <summary>Only unsent stages can be cancelled locally; already confirmed settings remain applied.</summary>
    public bool CanCancel => State.Phase is ManagedControlPhase.PendingConfiguration or ManagedControlPhase.PendingSearch;
    /// <summary>An unresolved action can be closed only after its uncertainty has been explicitly presented.</summary>
    public bool CanCloseUnverified => State.ReviewRequired && State.Phase is ManagedControlPhase.ConfigurationUncertain
        or ManagedControlPhase.SearchUncertain or ManagedControlPhase.AwaitingCommand;

    /// <summary>Accepts at least one explicit change or search for an already owned holding.</summary>
    public static ManagedControlOperation Create(Guid id, Guid connectionId, Guid holdingId, bool configure, bool search) {
        if (id == Guid.Empty || connectionId == Guid.Empty || holdingId == Guid.Empty || !configure && !search)
            throw new ArgumentException("A manager action requires stable identities and an explicit change or search.");
        return new(new(id, connectionId, holdingId, 1, configure, search,
            configure ? ManagedControlPhase.PendingConfiguration : ManagedControlPhase.PendingSearch));
    }

    /// <summary>Must be committed before sending a configuration mutation. Repeating this transition is forbidden.</summary>
    public void BeginConfiguration() {
        Require(ManagedControlPhase.PendingConfiguration);
        Change(State with { Phase = ManagedControlPhase.ConfigurationUncertain, ReviewRequired = false });
    }
    /// <summary>Confirms observed desired settings, including recovery by observation after a lost write response.</summary>
    public void ConfirmConfiguration() {
        if (State.Phase is not (ManagedControlPhase.PendingConfiguration or ManagedControlPhase.ConfigurationUncertain)) throw Invalid();
        Change(State with { ConfigurationConfirmed = true, ReviewRequired = false,
            Phase = State.SearchRequested ? ManagedControlPhase.PendingSearch : ManagedControlPhase.Completed });
    }
    /// <summary>Must be committed before requesting search. An uncertain search must never enter this transition again.</summary>
    public void BeginSearch() {
        Require(ManagedControlPhase.PendingSearch);
        Change(State with { Phase = ManagedControlPhase.SearchUncertain, ReviewRequired = false });
    }
    /// <summary>Records an exact returned command instead of guessing from recent upstream history.</summary>
    public void AcceptCommand(ManagedCommandIdentity command) {
        Require(ManagedControlPhase.SearchUncertain);
        if (string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > 512 || command.QueuedAt.Year < 1970) throw Invalid();
        Change(State with { Phase = ManagedControlPhase.AwaitingCommand, Command = command, ReviewRequired = false });
    }
    /// <summary>Tracks command execution only. Success makes no statement about downloads or local files.</summary>
    public void ObserveCommand(ManagedCommandIdentity command, ManagedCommandStatus status) {
        Require(ManagedControlPhase.AwaitingCommand);
        if (State.Command != command || !Enum.IsDefined(status)) throw Invalid();
        Change(State with { CommandStatus = status, ReviewRequired = status == ManagedCommandStatus.Unknown,
            Phase = status switch {
                ManagedCommandStatus.Completed => ManagedControlPhase.Completed,
                ManagedCommandStatus.Failed => ManagedControlPhase.Failed,
                ManagedCommandStatus.Cancelled => ManagedControlPhase.Cancelled,
                _ => ManagedControlPhase.AwaitingCommand
            } });
    }
    /// <summary>Records a definite refusal of the current stage, preserving any previously confirmed configuration.</summary>
    public void Reject() {
        if (State.Phase is not (ManagedControlPhase.PendingConfiguration or ManagedControlPhase.ConfigurationUncertain
            or ManagedControlPhase.PendingSearch or ManagedControlPhase.SearchUncertain)) throw Invalid();
        Change(State with { Phase = ManagedControlPhase.Rejected, ReviewRequired = false });
    }
    /// <summary>Retains uncertain progress for explicit review without making it eligible for redispatch.</summary>
    public void RequireReview() {
        if (!IsActive) throw Invalid();
        Change(State with { ReviewRequired = true });
    }
    /// <summary>Stops the next unsent stage; it does not undo settings or release the holding's fulfillment owner.</summary>
    public void Cancel() {
        if (!CanCancel) throw Invalid();
        Change(State with { Phase = ManagedControlPhase.Cancelled, ReviewRequired = false });
    }
    /// <summary>User acknowledgement ends observation without claiming success or cancelling upstream work.</summary>
    public void CloseUnverified() {
        if (!CanCloseUnverified) throw Invalid();
        Change(State with { Phase = ManagedControlPhase.ClosedUnverified, ReviewRequired = false });
    }
    private void Require(ManagedControlPhase phase) { if (State.Phase != phase) throw Invalid(); }
    private void Change(ManagedControlOperationState next) => State = next with { Revision = State.Revision + 1 };
    private static InvalidOperationException Invalid() => new("This manager action cannot make that transition at its current phase.");
}
