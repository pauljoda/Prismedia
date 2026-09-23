using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Runs typed integration operations through the existing installed executable package boundary.</summary>
public sealed partial class IntegrationPluginGateway(PrismediaDbContext db, PluginCatalogService catalog,
    PluginProcessTransport transport) : IIntegrationPluginGateway, IIntegrationDiscoveryGateway, IIntegrationTransferGateway {
    /// <inheritdoc />
    public async Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken) =>
        (await FindDescriptorAsync(pluginId, cancellationToken))?.Manifest;

    /// <inheritdoc />
    public async Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken cancellationToken) {
        var descriptor = await FindDescriptorAsync(pluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        return await InvokeAsync<ConnectionProbeInput, ConnectionProbeResult>(descriptor, IntegrationOperation.Probe,
            connection, new ConnectionProbeInput(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CatalogPage> DiscoverAsync(string pluginId, IntegrationOperation operation, IntegrationConnectionContext connection,
        IntegrationDiscoveryInput input, CancellationToken cancellationToken) {
        if (operation is not (IntegrationOperation.Search or IntegrationOperation.Browse)) throw new ArgumentException("Invalid catalog operation.");
        var descriptor = await FindDescriptorAsync(pluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        return await InvokeAsync<IntegrationDiscoveryInput, CatalogPage>(descriptor, operation, connection, input, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResolvedSourceOffer> ResolveAsync(string pluginId, IntegrationConnectionContext connection,
        ResolveSourceOfferInput input, CancellationToken cancellationToken) {
        var descriptor = await FindDescriptorAsync(pluginId, cancellationToken)
            ?? throw new IntegrationInvocationException("The integration plugin is unavailable or disabled.");
        return await InvokeAsync<ResolveSourceOfferInput, ResolvedSourceOffer>(descriptor, IntegrationOperation.Resolve, connection, input, cancellationToken);
    }

    /// <summary>Executes one bounded typed call and validates protocol and invocation correlation before returning a result.</summary>
    internal async Task<TOutput> InvokeAsync<TInput, TOutput>(PluginDescriptor descriptor, IntegrationOperation operation,
        IntegrationConnectionContext connection, TInput input, CancellationToken cancellationToken) where TOutput : class {
        connection = connection with { Auth = IntegrationCredentialScope.ForManifest(descriptor.Manifest, connection.Auth) };
        if (operation is IntegrationOperation.GetLibraryItem or IntegrationOperation.LookupManaged
            or IntegrationOperation.ReconcileManaged or IntegrationOperation.ConfigureManaged
            or IntegrationOperation.RequestManaged) {
            var mounts = await db.ExternalLibraryMounts.AsNoTracking()
                .Where(mount => mount.ConnectionId == connection.Id)
                .Select(mount => new IntegrationLibraryMount(mount.RemoteRootId, mount.RemotePath, mount.LocalPath))
                .ToArrayAsync(cancellationToken);
            connection = connection with { LibraryMounts = mounts };
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PluginProcessTransport.MaximumInvocationDuration);
        var invocationId = Guid.NewGuid();
        var request = new IntegrationPluginRequest<TInput>(IntegrationProtocol.Name, IntegrationProtocol.CurrentVersion,
            invocationId, operation, connection, input);
        try {
            var process = await transport.RunAsync(descriptor, request, deadline.Token);
            if (process.ExitCode != 0) throw new IntegrationInvocationException(
                PluginProcessTransport.RedactError(process.StandardError, connection.Auth.Values) is { Length: > 0 } error
                    ? error : "The integration plugin process failed.");
            var response = JsonSerializer.Deserialize<IntegrationPluginResponse<TOutput>>(process.StandardOutput, PluginProcessTransport.JsonOptions);
            if (response is null || response.Protocol != IntegrationProtocol.Name
                || response.ProtocolVersion != IntegrationProtocol.CurrentVersion || response.InvocationId != invocationId)
                throw new IntegrationInvocationException("The integration plugin returned an incompatible or uncorrelated response.");
            if (!response.Ok || response.Result is null) {
                var code = operation == IntegrationOperation.GetLibraryItem
                    && response.ErrorCode is { } errorCode
                    && errorCode.TryDecodeAs<IntegrationErrorCode>(out var decoded)
                        ? decoded
                        : (IntegrationErrorCode?)null;
                throw new IntegrationInvocationException(
                    PluginProcessTransport.RedactError(response.Error, connection.Auth.Values) ?? "The integration plugin did not return a result.",
                    code);
            }
            return response.Result;
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new IntegrationInvocationException("The integration plugin timed out.");
        } catch (ProcessOutputLimitException) {
            throw new IntegrationInvocationException("The integration plugin exceeded its output limit.");
        } catch (Exception error) when (error is JsonException or ArgumentOutOfRangeException or InvalidDataException) {
            throw new IntegrationInvocationException("The integration plugin returned an invalid or oversized payload.");
        } catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) {
            throw new IntegrationInvocationException("The integration plugin executable could not be started or read.");
        }
    }

    private async Task<PluginDescriptor?> FindDescriptorAsync(string pluginId, CancellationToken cancellationToken) {
        if (!await db.ProviderConfigs.AsNoTracking().AnyAsync(row => row.ProviderCode == pluginId && row.Enabled, cancellationToken)) return null;
        var descriptor = await catalog.FindProviderAsync(pluginId, null, cancellationToken);
        return descriptor?.Manifest.Integration is not null && descriptor.Manifest.Runtime == DotnetPluginProcessRunner.Code ? descriptor : null;
    }
}
