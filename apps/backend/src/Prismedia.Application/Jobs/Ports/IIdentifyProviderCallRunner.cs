using System.Text.Json;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Jobs.Ports;

/// <summary>Runs one graph-owned provider expansion for one concrete Entity.</summary>
public interface IIdentifyProviderCallRunner {
    /// <summary>Resolves and merges one Entity, then appends its direct child calls to the same graph.</summary>
    Task RunAsync(
        IdentifyProviderCallPayload payload,
        JobContext context,
        bool isFinalAttempt,
        CancellationToken cancellationToken);
}

/// <summary>Durable input for one Entity-scoped identify provider call.</summary>
public sealed record IdentifyProviderCallPayload(
    Guid RootEntityId,
    Guid TargetEntityId,
    Guid? ParentEntityId,
    string Provider,
    IdentifyQuery? Query,
    IReadOnlyDictionary<string, string>? ParentExternalIds,
    bool HideNsfw,
    bool HydrateRelationships,
    string ExpectedProposalId) {
    public string ToJson() => JsonSerializer.Serialize(this);

    public static IdentifyProviderCallPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<IdentifyProviderCallPayload>(payloadJson)
            ?? throw new InvalidOperationException("Identify provider-call payload is missing or invalid.");
}
