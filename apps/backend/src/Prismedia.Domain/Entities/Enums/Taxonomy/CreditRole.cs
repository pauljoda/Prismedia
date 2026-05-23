namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of person credit roles that can be attached to an entity. Each member
/// declares its stable storage/contract code inline so <see cref="EnumCodec{TValue}"/>
/// derives the encode/decode mapping automatically.
/// </summary>
public enum CreditRole {
    /// <summary>Generic credit when a more specific role is not known.</summary>
    [Code("person")]
    Person,

    /// <summary>Actor or performer credit.</summary>
    [Code("actor")]
    Actor,

    /// <summary>Director credit.</summary>
    [Code("director")]
    Director,

    /// <summary>Writer credit.</summary>
    [Code("writer")]
    Writer,

    /// <summary>Producer credit.</summary>
    [Code("producer")]
    Producer,

    /// <summary>Creator credit.</summary>
    [Code("creator")]
    Creator,

    /// <summary>Artist credit.</summary>
    [Code("artist")]
    Artist,

    /// <summary>Narrator credit.</summary>
    [Code("narrator")]
    Narrator,

    /// <summary>Composer credit.</summary>
    [Code("composer")]
    Composer
}
