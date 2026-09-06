using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class EfAcquisitionStore {
    /// <summary>
    /// Finds the exact stored snapshot of a compatible typed checkpoint. Older payloads may omit optional
    /// fields that the current codec accepts, but reserialization adds them and breaks direct JSON equality.
    /// The caller still compares-and-swaps the original JSON and lifecycle state, preserving concurrency.
    /// Unknown members remain blocked rather than being silently discarded during normalization.
    /// </summary>
    private async Task<string?> ReadEquivalentCheckpointJsonAsync<T>(Guid acquisitionId, T expected,
        Func<string?, T?> decode, Func<T, string> encode, CancellationToken cancellationToken) where T : class {
        var raw = await db.Acquisitions.AsNoTracking().Where(row => row.Id == acquisitionId)
            .Select(row => row.ImportCheckpointJson).SingleOrDefaultAsync(cancellationToken);
        if (raw is null) return null;
        try {
            if (decode(raw) is not { } decoded) return null;
            var canonical = encode(decoded);
            if (!string.Equals(canonical, encode(expected), StringComparison.Ordinal)) return null;
            using var original = JsonDocument.Parse(raw);
            using var normalized = JsonDocument.Parse(canonical);
            return HasUnknownMembers(original.RootElement, normalized.RootElement) ? null : raw;
        } catch (InvalidDataException) {
            return null;
        }
    }

    private static bool HasUnknownMembers(JsonElement original, JsonElement normalized) {
        if (original.ValueKind == JsonValueKind.Object) {
            if (normalized.ValueKind != JsonValueKind.Object) return true;
            foreach (var property in original.EnumerateObject()) {
                if (!normalized.TryGetProperty(property.Name, out var known)
                    || HasUnknownMembers(property.Value, known)) return true;
            }
        } else if (original.ValueKind == JsonValueKind.Array) {
            if (normalized.ValueKind != JsonValueKind.Array || original.GetArrayLength() != normalized.GetArrayLength()) return true;
            for (var index = 0; index < original.GetArrayLength(); index++) {
                if (HasUnknownMembers(original[index], normalized[index])) return true;
            }
        }
        return false;
    }
}
