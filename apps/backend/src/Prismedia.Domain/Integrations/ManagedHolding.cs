using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// Owns the lifecycle of a holding retained from a connected manager or library: when it is observed, when it
/// needs a person, removal that keeps local files and history, and the explicit ownership handoff. What each
/// status permits comes from its <see cref="ManagedTrackingStatusDefinition"/>; stores persist the result.
/// </summary>
public sealed class ManagedHolding {
    #region Static Variables

    /// <summary>Delay between routine observations of a followed holding.</summary>
    public static readonly TimeSpan ObservationInterval = TimeSpan.FromMinutes(1);

    /// <summary>Delay before rechecking a holding that is waiting on its manager rather than on local bytes.</summary>
    public static readonly TimeSpan ManagerWaitInterval = TimeSpan.FromMinutes(5);

    private const int MaximumProblemLength = 4096;

    private const string UnverifiedProblem =
        "The connection could not be verified. Previous bindings are retained; no alternate acquisition was started.";

    private const string UnverifiedRemovalProblem =
        "The connection could not be verified. The last confirmed removal and local data were retained.";

    #endregion

    #region Variables

    /// <summary>Immutable lifecycle to persist with optimistic concurrency.</summary>
    public ManagedHoldingState State { get; private set; }

    /// <summary>Behavior of the current status.</summary>
    public ManagedTrackingStatusDefinition Status => ManagedTrackingStatusDefinition.For(State.Status);

    #endregion

    #region Constructors

    /// <summary>Rehydrates a persisted lifecycle, refusing one no transition could have produced.</summary>
    /// <exception cref="ArgumentException">The lifecycle lacks its identity or revision, or its release facts disagree with its status.</exception>
    public ManagedHolding(ManagedHoldingState state) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Id == Guid.Empty || state.Revision < 1) {
            throw new ArgumentException("A tracked holding requires a stable identity and a positive revision.", nameof(state));
        }

        var definition = ManagedTrackingStatusDefinition.For(state.Status);
        if (definition.FreezesHostActions && state.ReleaseOperationId is null) {
            throw new ArgumentException("A holding in an ownership handoff must retain its release operation.", nameof(state));
        }

        if (state.Status == ManagedTrackingStatus.Released != state.ReleasedAt.HasValue) {
            throw new ArgumentException("Only a released holding records when it was released.", nameof(state));
        }

        State = state;
    }

    #endregion

    #region Actions - Creation

    /// <summary>Accepts reviewed associations for an existing remote holding; the worker verifies them next.</summary>
    public static ManagedHolding Accept(Guid id, DateTimeOffset now) =>
        new(new(id, ManagedTrackingStatus.Pending, 1, LastCheckedAt: null, NextCheckAt: now, Problem: null));

    /// <summary>Retains the stable wanted targets of a new managed request before any of its files arrive.</summary>
    public static ManagedHolding AwaitFiles(Guid id, DateTimeOffset now) =>
        new(new(id, ManagedTrackingStatus.WaitingForFiles, 1, now, now + ManagerWaitInterval, Problem: null));

    #endregion

    #region Actions - Observation

    /// <summary>Returns the holding to waiting for its manager after another finite target was accepted.</summary>
    public void WaitForFiles(DateTimeOffset now) {
        Require(!Status.FreezesHostActions, "wait for more files");
        Change(State with {
            Status = ManagedTrackingStatus.WaitingForFiles,
            LastCheckedAt = now,
            NextCheckAt = now + ManagerWaitInterval,
            Problem = null
        });
    }

    /// <summary>
    /// Records a verified observation: the holding is followed when every retained target has a local file, and
    /// keeps waiting for files otherwise.
    /// </summary>
    public void Reconcile(bool everyTargetBound, DateTimeOffset now) {
        Require(!Status.FreezesHostActions, "record a verified observation");
        Change(State with {
            Status = everyTargetBound ? ManagedTrackingStatus.Tracking : ManagedTrackingStatus.WaitingForFiles,
            LastCheckedAt = now,
            NextCheckAt = now + ObservationInterval,
            Problem = null
        });
    }

    /// <summary>Records provider-confirmed absence; local identities, readable files, and the fulfillment fence remain.</summary>
    public void ConfirmRemoval(string problem, DateTimeOffset now) {
        Require(!Status.FreezesHostActions, "record its removal");
        ChangeProblem(ManagedTrackingStatus.Removed, problem, now);
    }

    /// <summary>
    /// A removed remote identity that reappears is evidence for review, not a reversal of the confirmed removal:
    /// the holding stays removed so release remains available and nothing re-adopts files or controls silently.
    /// </summary>
    public void RecordReappearance(string problem, DateTimeOffset now) {
        Require(State.Status == ManagedTrackingStatus.Removed, "record a reappearance");
        ChangeProblem(ManagedTrackingStatus.Removed, problem, now);
    }

    /// <summary>Holds the holding for an explicit decision about identity, coverage, or source ownership.</summary>
    public void RequireReview(string problem, DateTimeOffset now) {
        Require(!Status.FreezesHostActions, "be held for review");
        ChangeProblem(ManagedTrackingStatus.NeedsReview, problem, now);
    }

    /// <summary>Records that the connection could not be verified, retaining previous evidence and any confirmed removal.</summary>
    public void RecordUnverifiable(DateTimeOffset now) {
        Require(!Status.FreezesHostActions, "record an unverifiable connection");
        var retained = Status.KeepsStatusWhenUnverifiable;
        ChangeProblem(retained ? State.Status : ManagedTrackingStatus.Stale,
            retained ? UnverifiedRemovalProblem : UnverifiedProblem, now);
    }

    /// <summary>Moves the next scheduled observation without changing the lifecycle revision.</summary>
    public void ScheduleNextObservation(DateTimeOffset now) =>
        State = State with { NextCheckAt = now + ObservationInterval };

    #endregion

    #region Actions - Release

    /// <summary>Freezes host actions for an explicit ownership handoff and observes the manager immediately.</summary>
    public void BeginRelease(Guid operationId, DateTimeOffset now) {
        if (operationId == Guid.Empty) {
            throw new ArgumentException("An ownership release requires a stable operation identity.", nameof(operationId));
        }

        Require(!Status.FreezesHostActions, "begin an ownership release");
        Change(State with {
            Status = ManagedTrackingStatus.ReleasePending,
            ReleaseOperationId = operationId,
            NextCheckAt = now,
            Problem = null
        });
    }

    /// <summary>Keeps a pending release waiting on its manager and explains why.</summary>
    public void RecordReleaseProblem(string problem, DateTimeOffset now) {
        Require(State.Status == ManagedTrackingStatus.ReleasePending, "record a release problem");
        Change(State with {
            Problem = Bounded(problem),
            LastCheckedAt = now,
            NextCheckAt = now + ManagerWaitInterval
        });
    }

    /// <summary>Completes the verified handoff; files and history remain while active associations end.</summary>
    public void CompleteRelease(DateTimeOffset now) {
        Require(State.Status == ManagedTrackingStatus.ReleasePending, "complete its release");
        Change(State with {
            Status = ManagedTrackingStatus.Released,
            ReleasedAt = now,
            LastCheckedAt = now,
            Problem = null
        });
    }

    #endregion

    #region Actions - Transitions

    private void ChangeProblem(ManagedTrackingStatus status, string problem, DateTimeOffset now) =>
        Change(State with {
            Status = status,
            Problem = Bounded(problem),
            LastCheckedAt = now,
            NextCheckAt = now + ObservationInterval
        });

    private static string Bounded(string problem) {
        if (string.IsNullOrWhiteSpace(problem)) {
            throw new ArgumentException("A holding problem needs an explanation.", nameof(problem));
        }

        return problem.Length > MaximumProblemLength ? problem[..MaximumProblemLength] : problem;
    }

    private void Require(bool allowed, string action) {
        if (!allowed) {
            throw new InvalidOperationException($"A tracked holding that is {Status.Description} cannot {action}.");
        }
    }

    private void Change(ManagedHoldingState next) => State = next with { Revision = State.Revision + 1 };

    #endregion
}
