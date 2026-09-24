using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Durable manager action independent of queue or upstream command history.</summary>
public sealed record StoredManagedControl(ManagedControlOperation Operation, ManagedControlPlan Plan,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem);
