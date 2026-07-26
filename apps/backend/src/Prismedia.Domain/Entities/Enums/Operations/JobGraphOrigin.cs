namespace Prismedia.Domain.Entities;

/// <summary>Classifies the root trigger that owns a durable job graph.</summary>
public enum JobGraphOrigin {
    /// <summary>Non-interactive work governed by the configured background worker count.</summary>
    [Code("background")]
    Background,

    /// <summary>Entity-scoped work initiated by a user and scheduled through an independent lane.</summary>
    [Code("interactive")]
    Interactive
}
