namespace Prismedia.Contracts.Integrations;

/// <summary>Reviews one missing issue from an existing connected comic run without writing.</summary>
public sealed record ReviewManagedComicIssueInput(ManagedItemInput Item, string RemoteIssueId, string IssueLabel);

/// <summary>Exact issue evidence and mapped destination shown before a comic manager request.</summary>
public sealed record ReviewedManagedComicIssue(long ConnectionRevision, ManagedItemInput Item,
    string SeriesTitle, ManagedComicIssue Issue, ExternalLibraryMount Mount, ManagerOptions Options,
    ManagedLookupInput Work, ManagedItemSnapshot Existing, bool RunMonitored,
    ManagedRequestResponse? ExistingRequest);

/// <summary>Accepts one reviewed issue under the same operation ID across retries.</summary>
public sealed record CommitManagedComicIssueInput(Guid OperationId, long ExpectedConnectionRevision,
    Guid LibraryRootId, ManagedItemInput Item, string RemoteIssueId, string IssueLabel,
    string ComicVineIssueId, bool Monitored);

/// <summary>The shared comic work and its durable exact-issue manager request.</summary>
public sealed record CommitManagedComicIssueResponse(Guid SeriesEntityId, Guid IssueEntityId,
    ManagedRequestResponse Request);
