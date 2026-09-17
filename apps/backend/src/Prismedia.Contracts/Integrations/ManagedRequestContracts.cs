using Prismedia.Domain.Entities;
using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Integrations;

/// <summary>Previews an existing wanted identity and a configured read-only external library boundary.</summary>
public sealed record PreviewManagedRequestInput(Guid EntityId, Guid LibraryRootId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Exact reviewed work, mapped root, remote choices, and any existing external holding.</summary>
public sealed record ManagedRequestPreview(Guid EntityId, string Title, ManagedLookupInput Work,
    ExternalLibraryMount Mount, ManagerOptions Options, ManagedItemSnapshot? Existing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Explicit fulfillment intent. Profile applies to initial creation; existing holdings must already use that reviewed profile.</summary>
public sealed record CreateManagedRequestInput(Guid OperationId, Guid EntityId, Guid LibraryRootId,
    ManagedLookupInput ReviewedWork, string ProfileId, bool Monitored, bool Search,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Durable request progress; completion requires local files, independently of manager search progress.</summary>
public sealed record ManagedRequestResponse(Guid Id, Guid ConnectionId, Guid EntityId, Guid LibraryRootId,
    string Title, ManagedRequestPhase Phase, long Revision, string? RemoteId, bool Monitored, bool Search,
    bool ReviewRequired, bool CanCancel, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Revision-fenced cancellation of intent that has no possible creation effect.</summary>
public sealed record CancelManagedRequestInput(long ExpectedRevision);
