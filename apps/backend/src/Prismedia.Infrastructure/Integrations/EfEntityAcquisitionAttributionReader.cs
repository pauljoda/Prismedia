using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Projects accepted source statements from durable import receipts without contacting plugins or upstream services.</summary>
public sealed class EfEntityAcquisitionAttributionReader(PrismediaDbContext db, TransferPlanProtector protector)
    : IEntityAcquisitionAttributionReader {
    /// <inheritdoc />
    public async Task<AcquisitionAttributionCapability?> ReadAsync(Guid entityId, CancellationToken cancellationToken) {
        // Exact JSON containment selects receipts before any private plan is decrypted.
        var receipt = JsonSerializer.Serialize(new {
            Mode = IntegrationTransferMode.SourceDownload,
            Imports = new[] { new { EntityIds = new[] { entityId } } }
        }, PluginProcessTransport.JsonOptions);
        var rows = await db.IntegrationTransfers.AsNoTracking()
            .Where(row => row.Phase == IntegrationTransferPhase.Completed && EF.Functions.JsonContains(row.StateJson, receipt))
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id).Take(100)
            .Select(row => new { row.Id, row.ConnectionId, row.CreatedAt, row.ProtectedPlan })
            .ToArrayAsync(cancellationToken);
        var items = new List<EntityAcquisitionAttribution>();
        var unavailable = false;
        foreach (var row in rows) {
            try {
                var plan = JsonSerializer.Deserialize<IntegrationTransferPlan>(
                    protector.Unprotect(row.ConnectionId, row.Id, row.ProtectedPlan), PluginProcessTransport.JsonOptions);
                if (plan?.Source?.Publication?.Attribution is { } attribution)
                    items.Add(new(row.Id, row.CreatedAt, attribution));
            } catch (IntegrationTransferPlanUnavailableException) { unavailable = true; }
            catch (JsonException) { unavailable = true; }
        }
        return items.Count == 0 && !unavailable ? null : new(items, unavailable);
    }
}
