using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Prismedia.Application.Security;

namespace Prismedia.Infrastructure.Security;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by ASP.NET Core Identity's PBKDF2 hasher
/// (HMAC-SHA512, V3 format). The format is self-describing, so raising the iteration
/// count later upgrades stored hashes transparently via
/// <see cref="PasswordVerification.SuccessRehashNeeded"/> on login.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher {
    // OWASP-recommended PBKDF2-HMAC-SHA512 work factor as of 2025.
    private const int IterationCount = 210_000;

    private readonly PasswordHasher<object> _hasher = new(Options.Create(new PasswordHasherOptions {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = IterationCount
    }));

    private static readonly object HasherUser = new();

    /// <inheritdoc />
    public string Hash(string password) => _hasher.HashPassword(HasherUser, password);

    /// <inheritdoc />
    public PasswordVerification Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(HasherUser, hash, password) switch {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed
        };
}
