using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Authorized connection context for a specific declared operation, populated only immediately before invocation.</summary>
public sealed record AuthorizedIntegrationConnection(IntegrationConnection Connection, PluginManifest Manifest,
    IntegrationConnectionContext Context);
