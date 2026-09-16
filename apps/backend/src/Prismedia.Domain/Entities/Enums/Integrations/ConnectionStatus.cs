namespace Prismedia.Domain.Entities;

/// <summary>Whether a configured connection has verified usable remote capabilities.</summary>
public enum ConnectionStatus {
    /// <summary>Configuration must be tested before operations may run.</summary>
    [Code("unverified")] Unverified,
    /// <summary>The most recent probe verified usable support.</summary>
    [Code("ready")] Ready,
    /// <summary>The remote application could not be reached or authenticated.</summary>
    [Code("unavailable")] Unavailable,
    /// <summary>The endpoint now reports a different persistent installation identity.</summary>
    [Code("identity-changed")] IdentityChanged,
    /// <summary>The user disabled this connection.</summary>
    [Code("disabled")] Disabled
}
