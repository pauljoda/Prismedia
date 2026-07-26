namespace Prismedia.Application.Jobs.Handlers;

/// <summary>Stable graph-local signal keys for identify review workflows.</summary>
public static class IdentifyGraphSignals {
    public static string Review(Guid entityId) => $"identify-review:{entityId}";
}
