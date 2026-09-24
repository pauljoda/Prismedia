using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Exact command identity. Retain its full timestamp precision when persisting this record.</summary>
public sealed record ManagedCommandIdentity(string Id, DateTimeOffset QueuedAt);

/// <summary>Rehydratable intent progress; remote effects are fenced by a saved uncertain phase.</summary>
public sealed record ManagedControlOperationState(Guid OperationId, Guid ConnectionId, Guid HoldingId, long Revision,
    bool HasConfiguration, bool SearchRequested, ManagedControlPhase Phase, bool ConfigurationConfirmed = false,
    ManagedCommandIdentity? Command = null, ManagedCommandStatus? CommandStatus = null, bool ReviewRequired = false);

/// <summary>
/// Prevents implicit repeat mutations when the upstream manager offers no idempotency guarantee. What each
/// phase permits comes from its <see cref="ManagedControlPhaseDefinition"/>, and an observed command's
/// outcome from its <see cref="ManagedCommandStatusDefinition"/>.
/// </summary>
public sealed class ManagedControlOperation {
    #region Static Variables

    private const int MaximumCommandIdLength = 512;

    #endregion

    #region Variables

    /// <summary>Immutable progress saved with optimistic concurrency at every external boundary.</summary>
    public ManagedControlOperationState State { get; private set; }

    /// <summary>Behavior of the current phase.</summary>
    public ManagedControlPhaseDefinition Phase => ManagedControlPhaseDefinition.For(State.Phase);

    /// <summary>Whether this action still holds its exclusive control slot. This is separate from fulfillment ownership.</summary>
    public bool IsActive => Phase.IsActive;

    /// <summary>Only unsent stages can be cancelled locally; already confirmed settings remain applied.</summary>
    public bool CanCancel => Phase.CanCancel;

    /// <summary>An unresolved action can be closed only after its uncertainty has been explicitly presented.</summary>
    public bool CanCloseUnverified => State.ReviewRequired && Phase.IsUncertain;

    #endregion

    #region Constructors

    /// <summary>Rehydrates persisted progress, refusing a state that no transition could have produced.</summary>
    /// <exception cref="ArgumentException">The state lacks its identities, revision, or an explicit change or search.</exception>
    public ManagedControlOperation(ManagedControlOperationState state) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.OperationId == Guid.Empty || state.ConnectionId == Guid.Empty || state.HoldingId == Guid.Empty
            || state.Revision < 1 || !state.HasConfiguration && !state.SearchRequested) {
            throw new ArgumentException("A manager action requires stable identities, a positive revision, and a change or search.", nameof(state));
        }

        State = state;
    }

    #endregion

    #region Actions - Creation

    /// <summary>Accepts at least one explicit change or search for an already owned holding.</summary>
    public static ManagedControlOperation Create(Guid id, Guid connectionId, Guid holdingId, bool configure, bool search) {
        if (id == Guid.Empty || connectionId == Guid.Empty || holdingId == Guid.Empty || !configure && !search) {
            throw new ArgumentException("A manager action requires stable identities and an explicit change or search.");
        }

        return new(new(id, connectionId, holdingId, 1, configure, search,
            configure ? ManagedControlPhase.PendingConfiguration : ManagedControlPhase.PendingSearch));
    }

    #endregion

    #region Actions - Configuration

    /// <summary>Must be committed before sending a configuration mutation. Repeating this transition is forbidden.</summary>
    public void BeginConfiguration() {
        Require(State.Phase == ManagedControlPhase.PendingConfiguration, "send its settings change");
        Change(State with { Phase = ManagedControlPhase.ConfigurationUncertain, ReviewRequired = false });
    }

    /// <summary>Confirms observed desired settings, including recovery by observation after a lost write response.</summary>
    public void ConfirmConfiguration() {
        Require(Phase.AwaitsConfiguration, "confirm its settings");
        Change(State with {
            ConfigurationConfirmed = true,
            ReviewRequired = false,
            Phase = State.SearchRequested ? ManagedControlPhase.PendingSearch : ManagedControlPhase.Completed
        });
    }

    #endregion

    #region Actions - Search

    /// <summary>Must be committed before requesting search. An uncertain search must never enter this transition again.</summary>
    public void BeginSearch() {
        Require(State.Phase == ManagedControlPhase.PendingSearch, "request its search");
        Change(State with { Phase = ManagedControlPhase.SearchUncertain, ReviewRequired = false });
    }

    /// <summary>Records an exact returned command instead of guessing from recent upstream history.</summary>
    /// <exception cref="ArgumentException">The command identity is unusable.</exception>
    public void AcceptCommand(ManagedCommandIdentity command) {
        Require(State.Phase == ManagedControlPhase.SearchUncertain, "accept a search command");
        if (string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > MaximumCommandIdLength || command.QueuedAt.Year < 1970) {
            throw new ArgumentException("The manager returned an unusable search command identity.", nameof(command));
        }

        Change(State with { Phase = ManagedControlPhase.AwaitingCommand, Command = command, ReviewRequired = false });
    }

    /// <summary>Tracks command execution only. Success makes no statement about downloads or local files.</summary>
    /// <exception cref="ArgumentException">The observation is for a different command.</exception>
    public void ObserveCommand(ManagedCommandIdentity command, ManagedCommandStatus status) {
        Require(State.Phase == ManagedControlPhase.AwaitingCommand, "observe a search command");
        if (State.Command != command) {
            throw new ArgumentException("The observed command is not the one this action accepted.", nameof(command));
        }

        var outcome = ManagedCommandStatusDefinition.For(status);
        Change(State with {
            CommandStatus = status,
            ReviewRequired = outcome.RequiresReview,
            Phase = outcome.SettledPhase ?? ManagedControlPhase.AwaitingCommand
        });
    }

    #endregion

    #region Actions - Resolution

    /// <summary>Records a definite refusal of the current stage, preserving any previously confirmed configuration.</summary>
    public void Reject() {
        Require(Phase.CanBeRejected, "record a refusal");
        Change(State with { Phase = ManagedControlPhase.Rejected, ReviewRequired = false });
    }

    /// <summary>Retains uncertain progress for explicit review without making it eligible for redispatch.</summary>
    public void RequireReview() {
        Require(Phase.IsActive, "be held for review");
        Change(State with { ReviewRequired = true });
    }

    /// <summary>Stops the next unsent stage; it does not undo settings or release the holding's fulfillment owner.</summary>
    public void Cancel() {
        Require(Phase.CanCancel, "be cancelled");
        Change(State with { Phase = ManagedControlPhase.Cancelled, ReviewRequired = false });
    }

    /// <summary>User acknowledgement ends observation without claiming success or cancelling upstream work.</summary>
    public void CloseUnverified() {
        Require(CanCloseUnverified, "be closed without verification");
        Change(State with { Phase = ManagedControlPhase.ClosedUnverified, ReviewRequired = false });
    }

    #endregion

    #region Actions - Transitions

    private void Require(bool allowed, string action) {
        if (!allowed) {
            throw new InvalidOperationException($"A manager action that is {Phase.Description} cannot {action}.");
        }
    }

    private void Change(ManagedControlOperationState next) => State = next with { Revision = State.Revision + 1 };

    #endregion
}
