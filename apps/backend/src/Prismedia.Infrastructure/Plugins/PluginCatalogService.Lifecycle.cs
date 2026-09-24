using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Prismedia.Application.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class PluginCatalogService {
    private async Task RequireNoUnfinishedWorkAsync(string pluginId, bool allowAcceptedHoldingObservation,
        CancellationToken token) {
        var connections = _db.IntegrationConnections.Where(row => row.PluginId == pluginId).Select(row => row.Id);
        var blocked = await _db.IntegrationTransfers.AnyAsync(row => connections.Contains(row.ConnectionId)
                && !IntegrationTransferPhaseDefinition.Terminal.Contains(row.Phase), token)
            || await _db.ManagedControls.AnyAsync(row => connections.Contains(row.ConnectionId) && row.ActiveHoldingId != null, token)
            || await _db.ManagedRequests.AnyAsync(row => connections.Contains(row.ConnectionId)
                && ManagedRequestPhaseDefinition.AwaitingHolding.Contains(row.Phase), token)
            || await _db.ManagedHoldings.AnyAsync(row => connections.Contains(row.ConnectionId)
                && ManagedTrackingStatusDefinition.BlockingPluginChanges.Contains(row.Status), token);
        if (!blocked) {
            var observations = await _db.ManagedRequests.AsNoTracking()
                .Where(row => connections.Contains(row.ConnectionId) && row.Phase == ManagedRequestPhase.AwaitingFiles)
                .ToArrayAsync(token);
            if (observations.Length > 0) {
                var ids = observations.Select(row => row.Id).ToArray();
                var holdings = await _db.ManagedHoldings.AsNoTracking()
                    .Where(row => ids.Contains(row.Id))
                    .ToDictionaryAsync(row => row.Id, token);
                blocked = !allowAcceptedHoldingObservation || observations.Any(request => {
                    var state = JsonSerializer.Deserialize<ManagedRequestState>(
                        request.StateJson, PluginProcessTransport.JsonOptions);
                    return state is null
                        || state.OperationId != request.Id
                        || state.ConnectionId != request.ConnectionId
                        || state.Revision != request.Revision
                        || state.Phase != ManagedRequestPhase.AwaitingFiles
                        || string.IsNullOrWhiteSpace(state.RemoteId)
                        || !holdings.TryGetValue(request.Id, out var holding)
                        || holding.ConnectionId != request.ConnectionId
                        || holding.Status != ManagedTrackingStatus.WaitingForFiles
                        || holding.RemoteId != state.RemoteId;
                });
            }
        }
        if (blocked)
            throw new PluginInUseException("This plugin has unfinished transfers, manager requests, or library actions. Finish or resolve that work before changing its installed version.");
    }

    private async Task InvalidateConnectionProbesAsync(string pluginId, CancellationToken token) {
        var connections = await _db.IntegrationConnections.Where(row => row.PluginId == pluginId).ToArrayAsync(token);
        foreach (var connection in connections) {
            connection.Revision++;
            connection.Status = connection.Enabled ? ConnectionStatus.Unverified : ConnectionStatus.Disabled;
            connection.EffectiveCapabilitiesJson = "[]";
            connection.LastCheckedAt = null;
            connection.LastError = null;
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
