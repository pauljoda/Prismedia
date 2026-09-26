using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Looks up one Comic Vine run and the mapped roots where it can be added.</summary>
public sealed record ReviewManagedComicRunInput(ExternalIdentity Identity);

/// <summary>Exact run identity, fresh manager evidence, and mapped destination choices.</summary>
public sealed record ReviewedManagedComicRun(long ConnectionRevision, ManagedCandidate Candidate,
    IReadOnlyList<ExternalLibraryMount> Mounts, ManagedItemInput? Existing);

/// <summary>Adds an exact reviewed run with monitoring and automatic search off.</summary>
public sealed record CommitManagedComicRunInput(Guid OperationId, long ExpectedConnectionRevision,
    ExternalIdentity Identity, string ExpectedTitle, Guid MountId);

/// <summary>The connected run to open after the manager confirms its identity and root.</summary>
public sealed record CommitManagedComicRunResponse(ManagedItemInput Item, bool Created);
