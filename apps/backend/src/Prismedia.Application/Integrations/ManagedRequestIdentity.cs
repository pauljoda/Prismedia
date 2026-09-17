using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Canonical replay and identity comparisons independent of dictionary insertion order.</summary>
public static class ManagedRequestIdentity {
    /// <summary>Fingerprints the explicit accepted request, including its local target and desired manager behavior.</summary>
    public static string Fingerprint(CreateManagedRequestInput request) => Hash(request with { ReviewedWork = Normalize(request.ReviewedWork) });
    /// <summary>Compares the exact reviewed identity set without title guessing or namespace dropping.</summary>
    public static bool SameWork(ManagedLookupInput first, ManagedLookupInput second) => Hash(Normalize(first)) == Hash(Normalize(second));
    private static ManagedLookupInput Normalize(ManagedLookupInput work) => work with { ExternalIds = work.ExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary() };
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
