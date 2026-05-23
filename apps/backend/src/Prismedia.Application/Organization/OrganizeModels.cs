namespace Prismedia.Application.Organization;

/// <summary>
/// Lightweight projection of a watched media root used by the organize planner.
/// </summary>
public sealed record OrganizeLibraryRoot(Guid Id, string Path);

/// <summary>
/// Lightweight projection of an active domain entity used by the organize planner.
/// Carries only the columns the planner reads.
/// </summary>
public sealed record OrganizeEntityRow(
    Guid Id,
    string KindCode,
    string Title,
    Guid? ParentEntityId);

/// <summary>
/// Lightweight projection of the canonical source file for an entity.
/// </summary>
public sealed record OrganizeSourceFile(Guid EntityId, string Path);
