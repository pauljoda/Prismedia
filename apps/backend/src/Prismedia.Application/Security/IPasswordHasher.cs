namespace Prismedia.Application.Security;

/// <summary>Outcome of verifying a password candidate against a stored hash.</summary>
public enum PasswordVerification {
    /// <summary>The candidate does not match the stored hash.</summary>
    Failed,

    /// <summary>The candidate matches the stored hash.</summary>
    Success,

    /// <summary>
    /// The candidate matches, but the hash was produced with outdated parameters and
    /// should be recomputed with <see cref="IPasswordHasher.Hash"/> and stored again.
    /// </summary>
    SuccessRehashNeeded
}

/// <summary>
/// Port for one-way password hashing. Implementations must use a slow, salted,
/// self-describing format so parameters can be upgraded transparently over time.
/// </summary>
public interface IPasswordHasher {
    /// <summary>Hashes a plaintext password for storage.</summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext candidate against a stored hash.</summary>
    PasswordVerification Verify(string hash, string password);
}
