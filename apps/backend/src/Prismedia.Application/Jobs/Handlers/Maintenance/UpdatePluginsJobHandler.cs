using Microsoft.Extensions.Logging;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers.Maintenance;

/// <summary>
/// Checks the remote community index and installs every compatible update advertised for an
/// installed plugin. Providers are updated sequentially because the catalog owns one scoped unit
/// of work; an individual failure is recorded while the remaining providers still get their turn.
/// </summary>
[JobDefinition(JobType.UpdatePlugins, SingletonBehavior = JobSingletonBehavior.QueueWide)]
public sealed class UpdatePluginsJobHandler(
    IPluginCatalogService plugins,
    ILogger<UpdatePluginsJobHandler> logger) : IJobHandler {
    /// <inheritdoc />
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var updates = (await plugins.ListProvidersAsync(cancellationToken))
            .Where(provider => provider.Installed && provider.UpdateAvailable)
            .ToArray();
        if (updates.Length == 0) {
            await context.ReportProgressAsync(100, "Plugins are up to date", cancellationToken);
            return;
        }

        var updatedCount = 0;
        var failures = new List<(string Name, Exception Error)>();
        foreach (var provider in updates) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var updated = await plugins.UpdateAsync(provider.Id, cancellationToken);
                if (updated is null) {
                    throw new InvalidOperationException($"No compatible update artifact was found for '{provider.Id}'.");
                }

                updatedCount++;
                await context.ReportProgressAsync(
                    updatedCount * 100 / updates.Length,
                    $"Updated {provider.Name} to {updated.Version}",
                    cancellationToken);
            } catch (OperationCanceledException ex)
                when (cancellationToken.IsCancellationRequested || ex.CancellationToken == cancellationToken) {
                throw;
            } catch (Exception ex) {
                failures.Add((provider.Name, ex));
                logger.LogError(ex, "Automatic update failed for plugin {PluginId}.", provider.Id);
            }
        }

        if (failures.Count > 0) {
            throw new InvalidOperationException(
                $"Failed to update {failures.Count} plugin(s): {string.Join(", ", failures.Select(failure => failure.Name))}.",
                failures[0].Error);
        }

        logger.LogInformation("Automatically updated {Count} plugin(s).", updatedCount);
    }
}
