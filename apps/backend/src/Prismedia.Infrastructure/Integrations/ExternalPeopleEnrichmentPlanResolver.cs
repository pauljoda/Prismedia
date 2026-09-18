using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Application.Plugins;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

internal sealed record ExternalPeopleEnrichmentPlan(
    Guid HoldingId,
    Guid EntityId,
    EntityKind EntityKind,
    ManagedItemInput Item,
    string Fingerprint,
    AuthorizedIntegrationConnection? Manager,
    IReadOnlyList<PluginIdentityRoute> MetadataRoutes);

internal interface IExternalPeopleEnrichmentPlanResolver {
    Task<ExternalPeopleEnrichmentPlan?> ResolveAsync(Guid holdingId, CancellationToken cancellationToken);
}

/// <summary>Builds a network-free, exact-identity enrichment plan from current saved configuration.</summary>
internal sealed class ExternalPeopleEnrichmentPlanResolver(
    PrismediaDbContext db,
    SettingsService settings,
    IPluginIdentityRouter identityRouter,
    IIdentifyProviderService providers,
    IntegrationConnectionAccess connections) : IExternalPeopleEnrichmentPlanResolver {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;

    public async Task<ExternalPeopleEnrichmentPlan?> ResolveAsync(
        Guid holdingId,
        CancellationToken cancellationToken) {
        var holding = await db.ManagedHoldings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == holdingId && row.ReleasedAt == null, cancellationToken);
        if (holding is null || holding.Kind is not (EntityKind.Movie or EntityKind.VideoSeries)) {
            return null;
        }

        var config = await settings.GetAutoIdentifySettingsAsync(cancellationToken);
        if (!config.Enabled || !config.EntityKinds.Contains(
                AutoIdentifySelectorKind.Video.ToCode(),
                StringComparer.OrdinalIgnoreCase)) {
            return null;
        }

        var item = JsonSerializer.Deserialize<ManagedItemInput>(holding.ItemJson, Json)
            ?? throw new InvalidDataException("The connected holding has invalid identity state.");
        if (item.EntityKind != holding.Kind) {
            throw new InvalidDataException("The connected holding kind no longer matches its pinned identity.");
        }

        var identities = CanonicalIdentities(item.ExpectedExternalIds);
        var entityId = await ResolveRootEntityIdAsync(holdingId, holding.Kind, cancellationToken);
        if (entityId is null) {
            return null;
        }

        var routes = await ResolveConfiguredRoutesAsync(
            holding.Kind,
            identities,
            config.Providers,
            cancellationToken);
        var manager = await ResolveManagerAsync(holding.ConnectionId, holding.Kind, cancellationToken);
        if (manager is null && routes.Count == 0) {
            return null;
        }

        var sources = new List<string>();
        if (manager is not null) {
            sources.Add($"manager:{manager.Manifest.Id}:{manager.Manifest.Version}:{manager.Connection.State.Revision}");
        }
        sources.AddRange(routes.Select(route =>
            $"metadata:{route.Provider.Id}:{route.Provider.Version}:{route.ConfigurationRevision}:{route.Route.Identity.Namespace}:{route.Route.Identity.Value}"));
        var identityKey = string.Join('|', identities
            .OrderBy(identity => identity.Namespace, StringComparer.Ordinal)
            .ThenBy(identity => identity.Value, StringComparer.Ordinal)
            .Select(identity => $"{identity.Namespace}:{identity.Value}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{holding.Id:D}|{entityId:D}|{identityKey}|{string.Join('|', sources)}"))).ToLowerInvariant();

        return new ExternalPeopleEnrichmentPlan(
            holding.Id,
            entityId.Value,
            holding.Kind,
            item,
            fingerprint,
            manager,
            routes.Select(route => route.Route).ToArray());
    }

    private async Task<Guid?> ResolveRootEntityIdAsync(
        Guid holdingId,
        EntityKind kind,
        CancellationToken cancellationToken) {
        var entityIds = await db.ManagedSourceBindings.AsNoTracking()
            .Where(binding => binding.HoldingId == holdingId)
            .Select(binding => binding.EntityId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (entityIds.Length == 0) {
            return null;
        }

        var roots = new HashSet<Guid>();
        foreach (var entityId in entityIds) {
            var currentId = entityId;
            var visited = new HashSet<Guid>();
            while (visited.Add(currentId)) {
                var entity = await db.Entities.AsNoTracking()
                    .Where(row => row.Id == currentId)
                    .Select(row => new { row.Id, row.KindCode, row.ParentEntityId })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidDataException("A connected holding references a missing local entity.");
                if (entity.KindCode == kind.ToCode()) {
                    roots.Add(entity.Id);
                    break;
                }
                if (entity.ParentEntityId is not { } parentId) {
                    throw new ArgumentException("The connected holding does not resolve to one matching local metadata root.");
                }
                currentId = parentId;
            }
        }

        return roots.Count switch {
            1 => roots.Single(),
            _ => throw new ArgumentException("The connected holding spans multiple local metadata roots. Review its associations before enrichment.")
        };
    }

    private async Task<IReadOnlyList<(PluginIdentityRoute Route, PluginProvider Provider, long ConfigurationRevision)>> ResolveConfiguredRoutesAsync(
        EntityKind kind,
        IReadOnlyList<ExternalIdentity> identities,
        IReadOnlyList<string> configuredProviders,
        CancellationToken cancellationToken) {
        var exactRoutes = await identityRouter.ResolveAsync(
            kind.ToCode(),
            IdentifyAction.LookupId,
            identities,
            cancellationToken);
        var available = (await providers.ListProvidersAsync(kind.ToCode(), cancellationToken))
            .Where(provider => provider is { Installed: true, Enabled: true, MissingAuthKeys.Count: 0 })
            .ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
        var providerIds = available.Keys.ToArray();
        var configurationRevisions = await db.ProviderConfigs.AsNoTracking()
            .Where(config => providerIds.Contains(config.ProviderCode))
            .ToDictionaryAsync(
                config => config.ProviderCode,
                config => config.UpdatedAt.UtcTicks,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);
        var result = new List<(PluginIdentityRoute, PluginProvider, long)>();
        foreach (var providerId in configuredProviders.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!available.TryGetValue(providerId, out var provider)
                || !configurationRevisions.TryGetValue(providerId, out var configurationRevision)) {
                continue;
            }
            result.AddRange(exactRoutes
                .Where(route => route.PluginId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(route => route.Identity.Namespace, StringComparer.Ordinal)
                .ThenBy(route => route.Identity.Value, StringComparer.Ordinal)
                .Select(route => (route, provider, configurationRevision)));
        }
        return result;
    }

    private async Task<AuthorizedIntegrationConnection?> ResolveManagerAsync(
        Guid connectionId,
        EntityKind kind,
        CancellationToken cancellationToken) {
        try {
            return await connections.RequireAsync(
                connectionId,
                PluginCapability.ExternalManager,
                IntegrationOperation.LookupManaged,
                kind,
                cancellationToken);
        } catch (Exception exception) when (exception is ConnectionNotFoundException
            or ConnectionSecretUnavailableException
            or ConnectionCapabilityUnavailableException
            or IntegrationInvocationException) {
            return null;
        }
    }

    private static IReadOnlyList<ExternalIdentity> CanonicalIdentities(
        IReadOnlyDictionary<string, string> values) {
        if (values is not { Count: > 0 }) {
            throw new ArgumentException("The connected holding has no pinned metadata identity.");
        }
        try {
            return values.Select(pair => new ExternalIdentity(pair.Key, pair.Value)).Distinct().ToArray();
        } catch (ArgumentException exception) {
            throw new ArgumentException("The connected holding has an invalid pinned metadata identity.", exception);
        }
    }
}
