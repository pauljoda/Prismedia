using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Security;

/// <summary>Runs the idempotent credential format upgrade after schema readiness and before serving work.</summary>
public static class ProviderCredentialUpgradeRunner {
    /// <summary>Uses a fresh unit of work; API and worker may safely perform this upgrade concurrently.</summary>
    public static async Task UpgradeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken token = default) {
        if (!PrismediaMigrationRunner.ShouldApply(configuration)) return;
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProviderCredentialStore>().UpgradeLegacyAsync(token);
    }
}
