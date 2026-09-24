using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Immutable reviewed mutation intent, containing no credentials.</summary>
public sealed record ManagedControlPlan(ManagedControlScope Scope, CreateManagedControlRequest Request, string RequestFingerprint,
    IReadOnlyList<Guid>? ScopeEntityIds = null);
