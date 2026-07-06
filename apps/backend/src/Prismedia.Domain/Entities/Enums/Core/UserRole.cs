namespace Prismedia.Domain.Entities;

/// <summary>
/// Authorization role of a Prismedia user account. Admins manage the server (users,
/// libraries, settings, acquisition) and implicitly see every library; members see only
/// the libraries they have been granted access to.
/// </summary>
public enum UserRole {
    /// <summary>Full server management plus implicit access to every library.</summary>
    [Code("admin")]
    Admin,

    /// <summary>Regular account restricted to explicitly granted libraries.</summary>
    [Code("member")]
    Member
}
