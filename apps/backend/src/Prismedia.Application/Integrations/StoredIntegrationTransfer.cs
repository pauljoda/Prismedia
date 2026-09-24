using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Loaded work and its immutable accepted intent, independent of queue history retention.</summary>
public sealed record StoredIntegrationTransfer(IntegrationTransfer Transfer, IntegrationTransferPlan Plan,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? LastError = null);
