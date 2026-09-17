using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class PluginCatalogService {
    private async Task RequireNoUnfinishedWorkAsync(string pluginId, CancellationToken token) {
        var connections = _db.IntegrationConnections.Where(row => row.PluginId == pluginId).Select(row => row.Id);
        if (await _db.IntegrationTransfers.AnyAsync(row => connections.Contains(row.ConnectionId)
                && row.Phase != IntegrationTransferPhase.Completed && row.Phase != IntegrationTransferPhase.Cancelled && row.Phase != IntegrationTransferPhase.Failed, token)
            || await _db.ManagedControls.AnyAsync(row => connections.Contains(row.ConnectionId) && row.ActiveHoldingId != null, token)
            || await _db.ManagedRequests.AnyAsync(row => connections.Contains(row.ConnectionId)
                && (row.Phase == ManagedRequestPhase.PendingCreation || row.Phase == ManagedRequestPhase.CreationUncertain || row.Phase == ManagedRequestPhase.AwaitingFiles), token)
            || await _db.ManagedHoldings.AnyAsync(row => connections.Contains(row.ConnectionId)
                && (row.Status == ManagedTrackingStatus.Pending || row.Status == ManagedTrackingStatus.ReleasePending), token))
            throw new PluginInUseException("This plugin has unfinished transfers, manager requests, or library actions. Finish or resolve that work before changing its installed version.");
    }

    private async Task InvalidateConnectionProbesAsync(string pluginId, CancellationToken token) {
        var connections = await _db.IntegrationConnections.Where(row => row.PluginId == pluginId).ToArrayAsync(token);
        foreach (var connection in connections) {
            connection.Revision++;
            connection.Status = connection.Enabled ? ConnectionStatus.Unverified : ConnectionStatus.Disabled;
            connection.EffectiveCapabilitiesJson = "[]";
            connection.LastCheckedAt = null;
            connection.LastError = "The plugin was updated. Test this connection again before using it.";
        }
    }

    private async Task SealInstalledSelectionAsync(string pluginId, CancellationToken token) {
        await using var transaction = await PluginLifecycleLease.AcquireAsync(_db, pluginId, token);
        var config = await _db.ProviderConfigs.FirstOrDefaultAsync(row => row.ProviderCode == pluginId, token);
        if (config is not null) {
            await _db.Entry(config).ReloadAsync(token);
            if (ReadInstalledSettings(config)?.Version is null && await FindProviderAsync(pluginId, null, token) is { } descriptor)
                await ActivateAsync(descriptor, token);
        }
        if (transaction is not null) await transaction.CommitAsync(token);
    }
}
