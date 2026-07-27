namespace Prismedia.Domain.Entities;

/// <summary>
/// Canonical semantic types for dates attached to an Entity. Metadata providers use these values to
/// distinguish availability milestones from biographical and general publication dates, allowing
/// acquisition profiles and calendar projections to reason about dates without provider-specific keys.
/// </summary>
public enum EntityDateType {
    /// <summary>The work was publicly announced.</summary>
    [Code("announcement")]
    Announcement,

    /// <summary>The work first premiered at any venue or service.</summary>
    [Code("premiere")]
    Premiere,

    /// <summary>The work opens in cinemas.</summary>
    [Code("theatrical-release")]
    TheatricalRelease,

    /// <summary>The work becomes available through a subscription streaming service.</summary>
    [Code("streaming-release")]
    StreamingRelease,

    /// <summary>The work becomes available through digital purchase or rental.</summary>
    [Code("digital-release")]
    DigitalRelease,

    /// <summary>The work becomes available on physical media.</summary>
    [Code("physical-release")]
    PhysicalRelease,

    /// <summary>An episode, programme, or other broadcast unit airs.</summary>
    [Code("air")]
    Air,

    /// <summary>A series first airs.</summary>
    [Code("first-air")]
    FirstAir,

    /// <summary>A series most recently or finally airs.</summary>
    [Code("last-air")]
    LastAir,

    /// <summary>A written work is published.</summary>
    [Code("publication")]
    Publication,

    /// <summary>A general release date when the provider cannot express a more specific milestone.</summary>
    [Code("release")]
    Release,

    /// <summary>A person's birth date.</summary>
    [Code("birth")]
    Birth,

    /// <summary>A person's death date.</summary>
    [Code("death")]
    Death,

    /// <summary>The start of a person's or group's active career.</summary>
    [Code("career-start")]
    CareerStart,

    /// <summary>The end of a person's or group's active career.</summary>
    [Code("career-end")]
    CareerEnd
}

/// <summary>
/// Compatibility vocabulary for legacy plugin date dictionaries. These aliases exist only at the
/// metadata boundary; persisted dates use <see cref="EntityDateType"/> codes.
/// </summary>
public static class EntityDateLegacyCodes {
    public const string Released = "released";
    public const string Date = "date";
    public const string Aired = "aired";
    public const string AirDate = "airDate";
    public const string FirstAir = "firstAir";
    public const string LastAir = "lastAir";
    public const string Published = "published";
    public const string Theatrical = "theatrical";
    public const string Streaming = "streaming";
    public const string Digital = "digital";
    public const string Physical = "physical";
}

/// <summary>Resolves canonical date codes and the compatibility aliases accepted from older plugins.</summary>
public static class EntityDateTypeRegistry {
    /// <summary>Returns the canonical type for a canonical or legacy code, or null for an open provider key.</summary>
    public static EntityDateType? Decode(string? code) {
        if (string.IsNullOrWhiteSpace(code)) {
            return null;
        }

        var trimmed = code.Trim();
        if (trimmed.TryDecodeAs<EntityDateType>(out var canonical)) {
            return canonical;
        }

        return trimmed switch {
            EntityDateLegacyCodes.Released or EntityDateLegacyCodes.Date => EntityDateType.Release,
            EntityDateLegacyCodes.Aired or EntityDateLegacyCodes.AirDate => EntityDateType.Air,
            EntityDateLegacyCodes.FirstAir => EntityDateType.FirstAir,
            EntityDateLegacyCodes.LastAir => EntityDateType.LastAir,
            EntityDateLegacyCodes.Published => EntityDateType.Publication,
            EntityDateLegacyCodes.Theatrical => EntityDateType.TheatricalRelease,
            EntityDateLegacyCodes.Streaming => EntityDateType.StreamingRelease,
            EntityDateLegacyCodes.Digital => EntityDateType.DigitalRelease,
            EntityDateLegacyCodes.Physical => EntityDateType.PhysicalRelease,
            _ => null
        };
    }

    /// <summary>Returns the canonical code for known types and preserves unknown legacy provider keys.</summary>
    public static string NormalizeCode(string code) => Decode(code)?.ToCode() ?? code.Trim();
}
