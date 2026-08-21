using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>
/// Declares definition-owned participation in an ordered media sequence. The capability is derived
/// from the same topology used for progress so client layout and server roll-up cannot drift.
/// </summary>
/// <param name="Role">Whether this Entity owns the sequence or is one of its ordered items.</param>
/// <param name="ItemKind">The one Entity kind ordered by this sequence family.</param>
/// <param name="ContainerKinds">Valid roll-up container kinds for an item; empty for a container.</param>
[CapabilityKind("ordered-sequence")]
public sealed record OrderedSequenceCapability(
    EntitySequenceRole Role,
    EntityKind ItemKind,
    IReadOnlyList<EntityKind> ContainerKinds) : EntityCapability;
