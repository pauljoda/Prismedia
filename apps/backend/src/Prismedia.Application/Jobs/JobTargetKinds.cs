namespace Prismedia.Application.Jobs;

/// <summary>
/// Stable queue target-kind codes for operational jobs that are not tied to one domain entity kind.
/// </summary>
public static class JobTargetKinds {
    /// <summary>Generic entity refresh target used when the handler fans out by the persisted entity kind.</summary>
    public const string Entity = "entity";

    /// <summary>Library-root scoped target used by scan jobs and their tests.</summary>
    public const string LibraryRoot = "library-root";
}
