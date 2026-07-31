namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of non-structural relationship codes stored on
/// <c>EntityRelationshipLinkRow.RelationshipCode</c> and used to group an entity's
/// <see cref="Entity.Relationships"/> by intent. Each member declares its stable
/// storage/contract code inline so <see cref="EnumCodec{TValue}"/> derives the
/// encode/decode mapping automatically.
/// </summary>
/// <remarks>
/// These codes describe <em>how</em> a related entity is linked, independent of the
/// related entity's own <see cref="EntityKind"/>. Provider-specific aliases (for example
/// the Stash <c>performer</c> input) are normalized to one of these codes at the
/// integration boundary rather than being added as members here.
/// </remarks>
[CodeFamily("RELATIONSHIP_CODE", "RelationshipCode")]
public enum RelationshipKind {
    /// <summary>People credited on the entity (cast and crew); role detail lives in link metadata.</summary>
    [Code("cast")]
    Cast,

    /// <summary>Credit links projected as credit metadata (role/character) on detail surfaces.</summary>
    [Code("credits")]
    Credits,

    /// <summary>Studios, publishers, or labels associated with the entity.</summary>
    [Code("studio")]
    Studio,

    /// <summary>Tags applied to the entity.</summary>
    [Code("tags")]
    Tags,

    /// <summary>Generic "related to" link between entities with no stronger semantic.</summary>
    [Code("related")]
    Related
}
