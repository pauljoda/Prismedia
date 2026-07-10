namespace Prismedia.Contracts.Entities;

/// <summary>
/// Declares that an Entity is a safe root for the shared managed-file deletion workflow. The workflow
/// removes source paths for the full structural subtree and reconciles linked acquisition state; kinds
/// whose paths are virtual archive members deliberately do not expose this capability.
/// </summary>
/// <param name="CanDeleteFiles">Whether the Entity may invoke managed delete-files.</param>
[CapabilityKind("file-management")]
public sealed record FileManagementCapability(bool CanDeleteFiles) : EntityCapability;
