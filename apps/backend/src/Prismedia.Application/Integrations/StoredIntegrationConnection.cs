using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Connection aggregate plus write-only credential presence, without exposing credential values.</summary>
public sealed record StoredIntegrationConnection(IntegrationConnection Connection, IReadOnlyList<string> ConfiguredSecretKeys);
