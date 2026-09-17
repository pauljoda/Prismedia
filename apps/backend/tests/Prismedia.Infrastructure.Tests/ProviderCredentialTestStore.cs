using Microsoft.AspNetCore.DataProtection;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Shares an ephemeral test key ring while keeping provider/key binding equivalent to production.</summary>
internal static class ProviderCredentialTestStore {
    private static readonly ProviderCredentialProtector Protector = new(new EphemeralDataProtectionProvider());
    internal static ProviderCredentialStore Create(PrismediaDbContext db) => new(db, Protector);
}
