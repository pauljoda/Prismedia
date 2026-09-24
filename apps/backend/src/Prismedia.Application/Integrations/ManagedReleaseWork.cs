using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Accepted release intent and immutable scope evidence retained while ownership is reserved.</summary>
public sealed record ManagedReleaseWork(ManagedTrackingResponse Holding, ReleaseManagedHoldingRequest Request);
