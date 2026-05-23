namespace Prismedia.Contracts.Organize;

/// <summary>
/// Request for computing or applying an entity organization plan.
/// </summary>
/// <param name="EntityId">Optional single entity scope.</param>
/// <param name="RootId">Optional library root scope.</param>
public sealed record OrganizePlanRequest(Guid? EntityId, Guid? RootId);

/// <summary>
/// One filesystem operation proposed by the entity organizer.
/// </summary>
/// <param name="EntityId">Entity that owns the source path.</param>
/// <param name="Kind">Entity kind code.</param>
/// <param name="Title">Entity display title used to derive target names.</param>
/// <param name="StorageShape">Storage shape code that controlled planning.</param>
/// <param name="SourcePath">Current source file or folder path.</param>
/// <param name="TargetPath">Desired source file or folder path.</param>
/// <param name="Status">Operation status: ready, unchanged, skipped, applied, or failed.</param>
/// <param name="Reason">Optional human-readable reason for skipped or failed items.</param>
public sealed record OrganizePlanItem(
    Guid EntityId,
    string Kind,
    string Title,
    string StorageShape,
    string SourcePath,
    string TargetPath,
    string Status,
    string? Reason);

/// <summary>
/// Dry-run organization plan response.
/// </summary>
/// <param name="Items">Planned entity source path operations.</param>
public sealed record OrganizePlanResponse(IReadOnlyList<OrganizePlanItem> Items);

/// <summary>
/// Apply response for organization operations.
/// </summary>
/// <param name="Items">Final operation statuses after apply.</param>
/// <param name="Applied">Number of filesystem moves applied.</param>
/// <param name="Skipped">Number of operations skipped, unchanged, or failed.</param>
public sealed record OrganizeApplyResponse(
    IReadOnlyList<OrganizePlanItem> Items,
    int Applied,
    int Skipped);
