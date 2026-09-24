using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Existing connected scope and the explicit selections accepted before any worker effects.</summary>
public sealed record ManagedTrackingWork(ManagedTrackingResponse Tracking, IReadOnlyList<ManagedBindingSelection> Selections);
