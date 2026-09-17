namespace Prismedia.Contracts.Integrations;

/// <summary>Requests read-only activity evidence for an exact holding and finite acquisition scope.</summary>
public sealed record InspectManagedReleaseInput(ManagedControlScope Scope);
/// <summary>Current configuration and complete activity evidence; observation does not freeze the remote application.</summary>
public sealed record ManagedReleaseObservation(ManagedControlState State, bool QueueEmpty, bool CommandsIdle);
/// <summary>Reviewable handoff evidence bound to the retained holding revision and target identities.</summary>
public sealed record ManagedReleasePreview(long Revision, string ScopeFingerprint, ManagedReleaseObservation Observation,
    bool CanRelease, string? Problem);
/// <summary>Explicit, repeatable intent to stop tracking and release this holding's acquisition ownership.</summary>
public sealed record ReleaseManagedHoldingRequest(Guid OperationId, long ExpectedRevision, string ScopeFingerprint, string ExpectedPath);
