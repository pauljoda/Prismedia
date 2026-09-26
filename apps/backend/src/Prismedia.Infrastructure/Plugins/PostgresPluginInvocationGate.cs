using Microsoft.Extensions.Logging;
using Npgsql;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Uses short PostgreSQL transactions to admit API and worker calls against the same plugin-wide budget.</summary>
public sealed class PostgresPluginInvocationGate(NpgsqlDataSource dataSource, ILogger<PostgresPluginInvocationGate> logger)
    : IPluginInvocationGate {
    // The transport enforces a 60-second invocation deadline. Retain abandoned capacity for
    // twice that budget to allow cancellation and child-process cleanup before reuse.
    private static readonly TimeSpan LeaseDuration = PluginProcessTransport.MaximumInvocationDuration * 2;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireAsync(string pluginId, PluginExecutionPolicy policy, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId.Length > 128)
            throw new ArgumentException("A valid plugin identity is required.", nameof(pluginId));
        if (policy.MaxConcurrentInvocations is < 1 or > 64 || policy.MinimumStartIntervalMs is < 0 or > 86_400_000)
            throw new ArgumentException("The plugin invocation policy is invalid.", nameof(policy));
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            if (await TryAcquireAsync(id, pluginId, policy, cancellationToken)) return new Lease(this, id);
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<bool> TryAcquireAsync(Guid id, string pluginId, PluginExecutionPolicy policy, CancellationToken token) {
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var prepare = new NpgsqlCommand("""
            INSERT INTO plugin_invocation_states (plugin_id, next_start_at)
            VALUES (@plugin, clock_timestamp()) ON CONFLICT (plugin_id) DO NOTHING;
            SELECT next_start_at FROM plugin_invocation_states WHERE plugin_id = @plugin FOR UPDATE;
            DELETE FROM plugin_invocation_leases WHERE plugin_id = @plugin AND expires_at <= clock_timestamp();
            """, connection, transaction)) {
            prepare.Parameters.AddWithValue("plugin", pluginId);
            await prepare.ExecuteNonQueryAsync(token);
        }
        await using var claim = new NpgsqlCommand("""
            INSERT INTO plugin_invocation_leases (id, plugin_id, expires_at)
            SELECT @id, @plugin, clock_timestamp() + @lifetime
            WHERE (SELECT COUNT(*) FROM plugin_invocation_leases WHERE plugin_id = @plugin) < @capacity
              AND (SELECT next_start_at FROM plugin_invocation_states WHERE plugin_id = @plugin) <= clock_timestamp();
            """, connection, transaction);
        claim.Parameters.AddWithValue("id", id);
        claim.Parameters.AddWithValue("plugin", pluginId);
        claim.Parameters.AddWithValue("lifetime", LeaseDuration);
        claim.Parameters.AddWithValue("capacity", policy.MaxConcurrentInvocations);
        if (await claim.ExecuteNonQueryAsync(token) == 0) {
            await transaction.CommitAsync(token);
            return false;
        }
        await using (var pace = new NpgsqlCommand("""
            UPDATE plugin_invocation_states SET next_start_at = clock_timestamp() + @interval WHERE plugin_id = @plugin;
            """, connection, transaction)) {
            pace.Parameters.AddWithValue("plugin", pluginId);
            pace.Parameters.AddWithValue("interval", TimeSpan.FromMilliseconds(policy.MinimumStartIntervalMs));
            await pace.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return true;
    }

    private async ValueTask ReleaseAsync(Guid id) {
        try {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var connection = await dataSource.OpenConnectionAsync(budget.Token);
            await using var release = new NpgsqlCommand("DELETE FROM plugin_invocation_leases WHERE id = @id;", connection);
            release.Parameters.AddWithValue("id", id);
            await release.ExecuteNonQueryAsync(budget.Token);
        } catch (Exception error) when (error is NpgsqlException or OperationCanceledException or TimeoutException) {
            logger.LogWarning("Could not release plugin invocation lease {LeaseId}; its bounded reservation will expire.", id);
        }
    }

    private sealed class Lease(PostgresPluginInvocationGate gate, Guid id) : IAsyncDisposable {
        private int disposed;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref disposed, 1) == 0 ? gate.ReleaseAsync(id) : ValueTask.CompletedTask;
    }
}
