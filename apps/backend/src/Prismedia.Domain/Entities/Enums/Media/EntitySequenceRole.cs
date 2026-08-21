namespace Prismedia.Domain.Entities;

/// <summary>
/// Describes how an Entity participates in a definition-owned ordered sequence.
/// </summary>
public enum EntitySequenceRole {
    /// <summary>The Entity owns an ordered sequence of one declared item kind.</summary>
    [Code("container")]
    Container,

    /// <summary>The Entity is an ordered item that may roll progress into declared containers.</summary>
    [Code("item")]
    Item
}
