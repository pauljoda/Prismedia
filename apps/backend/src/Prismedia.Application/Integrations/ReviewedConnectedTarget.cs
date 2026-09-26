using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Write-free evidence for one exact target chosen inside a connected holding.</summary>
/// <param name="Title">Display title of the holding's work.</param>
/// <param name="Work">Exact manager lookup identity of the work, naming the chosen target.</param>
/// <param name="Targets">The chosen targets with the manager's own evidence for them.</param>
public sealed record ReviewedConnectedTarget(
    string Title,
    ManagedLookupInput Work,
    IReadOnlyList<ReviewedManagedTarget> Targets);
