using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Canonical identities for reviewed scope and replayed intent, independent of mutable file locations.</summary>
public static class ManagedControlIdentity {
    /// <summary>Includes pinned work IDs, exact target coordinates, local owners, connection, and mapped root.</summary>
    public static OwnedManagedControlScope From(ManagedTrackingResponse holding) {
        var bindings = holding.Targets.OrderBy(item => item.Target.RemoteTargetId, StringComparer.Ordinal).ToArray();
        var item = holding.Item with { ExpectedExternalIds = holding.Item.ExpectedExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary() };
        var targets = bindings.Select(binding => new ManagedControlTarget(binding.Target.RemoteTargetId, binding.Target.Kind,
            binding.Target.SeasonNumber, binding.Target.EpisodeNumber, binding.Target.AbsoluteNumber)).ToArray();
        return new(new(item, targets), Hash(new { holding.Id, holding.ConnectionId, holding.LibraryRootId, Item = item,
            Bindings = bindings.Select(binding => new { binding.Target, binding.EntityId }) }));
    }
    /// <summary>Normalizes dictionary ordering before comparing repeated client operation IDs.</summary>
    public static string RequestFingerprint(CreateManagedControlRequest request) => Hash(request with {
        ExpectedMonitoring = request.ExpectedMonitoring.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary()
    });
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
