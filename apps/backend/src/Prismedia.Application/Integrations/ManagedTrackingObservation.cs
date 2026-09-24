using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Independently mapped bytes and existing local source owners observed for a connected holding.</summary>
public sealed record ManagedTrackingObservation(Guid? LibraryRootId, IReadOnlyList<ManagedObservedFile> Files,
    IReadOnlyList<ManagedLocalSource> Sources);
