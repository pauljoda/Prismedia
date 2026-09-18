using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Requests;

/// <summary>Validated, write-free manager lookup evidence derived from a complete metadata review.</summary>
internal sealed record ReviewedWantedPlan(string Title, ManagedLookupInput Work);
