namespace Prismedia.Domain.Entities;

/// <summary>
/// The search context contribution an Entity kind makes when a graph-backed acquisition walks its
/// structural ancestors. The nearest ancestor for each role wins.
/// </summary>
public enum AcquisitionAncestorContextRole {
    /// <summary>The kind does not contribute acquisition search context.</summary>
    None,

    /// <summary>The kind contributes its title as the acquisition creator or artist.</summary>
    Creator,

    /// <summary>The kind contributes its title as the acquisition series or containing work.</summary>
    Series
}
