using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>
/// An existing Entity and its contained scope. Book renditions have independent owners; an ordinary
/// Entity scope includes its descendants. Identity equivalence must use provider evidence, never titles.
/// </summary>
public sealed record FulfillmentScope {
    /// <summary>Creates an explicit, bounded ownership scope without conflating book renditions.</summary>
    public FulfillmentScope(Guid entityId, EntityKind kind, BookRendition? rendition) {
        if (entityId == Guid.Empty) throw new ArgumentException("Choose an existing item for acquisition ownership.");
        if ((kind == EntityKind.Book) != (rendition is not null) || rendition is not null && !Enum.IsDefined(rendition.Value))
            throw new ArgumentException("Book ownership requires an explicit rendition; other entities have no book rendition.");
        EntityId = entityId; Kind = kind; Rendition = rendition;
    }

    /// <summary>Canonical local identity retained through acquisition and source replacement.</summary>
    public Guid EntityId { get; }
    /// <summary>Media semantics of the scope.</summary>
    public EntityKind Kind { get; }
    /// <summary>Independent book rendition, or all descendants of an ordinary Entity scope.</summary>
    public BookRendition? Rendition { get; }
}
