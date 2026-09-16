using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Persists connection aggregates and encrypts credentials in the same optimistic transaction.</summary>
public sealed class EfIntegrationConnectionStore(PrismediaDbContext db, ConnectionSecretProtector secrets) : IIntegrationConnectionStore {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken) =>
        (await db.IntegrationConnections.AsNoTracking().OrderBy(row => row.Name).ToArrayAsync(cancellationToken)).Select(Map).ToArray();

    /// <inheritdoc />
    public async Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken) is { } row ? Map(row) : null;

    /// <inheritdoc />
    public async Task SaveAsync(IntegrationConnection connection, long? expectedRevision,
        IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken) {
        await using var transaction = await LibraryRootConfigurationLease.AcquireAsync(db, cancellationToken);
        var state = connection.State;
        var row = await db.IntegrationConnections.SingleOrDefaultAsync(row => row.Id == state.Id, cancellationToken);
        if (expectedRevision is null) {
            if (row is not null || state.Revision != 1) throw new ConnectionConflictException();
            row = new IntegrationConnectionRow { Id = state.Id, PluginId = state.PluginId };
            db.IntegrationConnections.Add(row);
        } else if (row is null) throw new ConnectionNotFoundException();
        else if (row.Revision != expectedRevision || state.Revision != expectedRevision + 1) throw new ConnectionConflictException();

        if (expectedRevision is not null && (row.BaseUrl != state.BaseUrl
            || !Read<Dictionary<string, string>>(row.SettingsJson).OrderBy(pair => pair.Key).SequenceEqual(state.Settings.OrderBy(pair => pair.Key)))
            && await db.ExternalLibraryMounts.AnyAsync(mount => mount.ConnectionId == state.Id, cancellationToken))
            throw new ArgumentException("This connection owns mapped library roots. Create a separate connection for another address or source configuration.");
        var protectedValues = Read<Dictionary<string, string>>(row.ProtectedSecretsJson);
        foreach (var (key, value) in secretChanges) {
            if (string.IsNullOrEmpty(value)) protectedValues.Remove(key);
            else protectedValues[key] = secrets.Protect(state.Id, key, value);
        }
        row.Name = state.Name;
        row.BaseUrl = state.BaseUrl;
        row.Enabled = state.Enabled;
        row.Revision = state.Revision;
        row.Status = state.Status;
        row.SettingsJson = JsonSerializer.Serialize(state.Settings, Json);
        row.EnabledCapabilitiesJson = JsonSerializer.Serialize(state.EnabledCapabilities, Json);
        row.ProtectedSecretsJson = JsonSerializer.Serialize(protectedValues, Json);
        row.EffectiveCapabilitiesJson = JsonSerializer.Serialize(state.EffectiveCapabilities, Json);
        row.RemoteInstanceId = state.RemoteInstanceId;
        row.HasPersistentRemoteIdentity = state.HasPersistentRemoteIdentity;
        row.LastCheckedAt = state.LastCheckedAt;
        row.LastError = state.LastError;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConnectionConflictException(); }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new ConnectionNotFoundException();
        return Read<Dictionary<string, string>>(row.ProtectedSecretsJson).ToDictionary(pair => pair.Key,
            pair => secrets.Unprotect(id, pair.Key, pair.Value), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) {
        var row = await db.IntegrationConnections.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new ConnectionNotFoundException();
        if (row.Revision != expectedRevision) throw new ConnectionConflictException();
        if (await db.IntegrationTransfers.AsNoTracking().AnyAsync(transfer => transfer.ConnectionId == id, cancellationToken)
            || await db.ExternalLibraryMounts.AnyAsync(mount => mount.ConnectionId == id, cancellationToken)) throw new ConnectionInUseException();
        db.IntegrationConnections.Remove(row);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConnectionConflictException(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }) { throw new ConnectionInUseException(); }
    }

    private static T Read<T>(string json) where T : notnull => JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidDataException("Stored connection configuration is invalid.");

    private static StoredIntegrationConnection Map(IntegrationConnectionRow row) => new(new IntegrationConnection(new(
        row.Id, row.PluginId, row.Name, row.BaseUrl, row.Enabled,
        Read<PluginCapability[]>(row.EnabledCapabilitiesJson), Read<Dictionary<string, string>>(row.SettingsJson),
        row.Revision, row.Status, row.RemoteInstanceId, Read<IntegrationSupport[]>(row.EffectiveCapabilitiesJson),
        row.LastCheckedAt, row.LastError, row.HasPersistentRemoteIdentity)),
        Read<Dictionary<string, string>>(row.ProtectedSecretsJson).Keys.Order(StringComparer.Ordinal).ToArray());
}
