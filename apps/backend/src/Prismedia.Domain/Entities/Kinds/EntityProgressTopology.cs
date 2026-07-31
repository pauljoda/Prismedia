namespace Prismedia.Domain.Entities;

/// <summary>
/// Declares how a kind owns a user progress cursor. Definitions, rather than consumers, are the
/// source of truth so new kinds must make an explicit topology choice.
/// </summary>
public abstract record EntityProgressTopology {
    /// <summary>Kind has no progress cursor.</summary>
    public sealed record NoneTopology : EntityProgressTopology;

    /// <summary>Kind stores progress directly on itself and never rolls it up.</summary>
    public sealed record DirectTopology : EntityProgressTopology;

    /// <summary>Kind participates in one work cursor owned by the nearest declared work kind.</summary>
    public sealed record WorkTopology(EntityKind WorkKind, bool FallsBackToDirect) : EntityProgressTopology;

    /// <summary>Playable child contributes ordered progress to each declared container ancestor.</summary>
    public sealed record OrderedRollupTopology(EntityKind ItemKind, IReadOnlyList<EntityKind> ContainerKinds)
        : EntityProgressTopology;

    /// <summary>Container that owns an ordered cursor produced by its declared item kind.</summary>
    public sealed record OrderedContainerTopology(EntityKind ItemKind) : EntityProgressTopology;

    /// <summary>Singleton no-progress declaration.</summary>
    public static EntityProgressTopology None { get; } = new NoneTopology();

    /// <summary>Singleton direct-progress declaration.</summary>
    public static EntityProgressTopology Direct { get; } = new DirectTopology();

    /// <summary>Declares a work cursor owned by <paramref name="workKind"/>.</summary>
    public static EntityProgressTopology Work(EntityKind workKind, bool fallsBackToDirect = false) =>
        new WorkTopology(workKind, fallsBackToDirect);

    /// <summary>Declares ordered roll-up from an item into the supplied container kinds.</summary>
    public static EntityProgressTopology OrderedRollup(EntityKind itemKind, params EntityKind[] containerKinds) =>
        new OrderedRollupTopology(itemKind, Array.AsReadOnly(containerKinds));

    /// <summary>Declares an ordered progress container for <paramref name="itemKind"/>.</summary>
    public static EntityProgressTopology OrderedContainer(EntityKind itemKind) => new OrderedContainerTopology(itemKind);
}
