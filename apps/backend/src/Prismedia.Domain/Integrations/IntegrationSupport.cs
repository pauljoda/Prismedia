using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Usable operations and media kinds in a single responsibility family.</summary>
public sealed record IntegrationSupport(PluginCapability Kind, IReadOnlyList<IntegrationOperation> Operations,
    IReadOnlyList<EntityKind> EntityKinds);
