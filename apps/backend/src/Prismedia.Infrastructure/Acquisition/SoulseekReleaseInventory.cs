using System.Text.Json;
using Prismedia.Application.Acquisition;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Restores the file evidence embedded in a Soulseek release when an automatic grab rechecks its rules.</summary>
public sealed class SoulseekReleaseInventory : IAcquisitionReleaseInventory {
    /// <inheritdoc />
    public IReadOnlyList<string> ReadFileNames(string? downloadUrl) {
        if (downloadUrl?.StartsWith(SoulseekProtocol.LocatorPrefix, StringComparison.Ordinal) != true) return [];
        try {
            return SoulseekLocator.Decode(downloadUrl).Files?
                .Where(file => !string.IsNullOrWhiteSpace(file.Filename)).Select(file => file.Filename).ToArray() ?? [];
        } catch (Exception exception) when (exception is FormatException or JsonException or InvalidDataException) {
            return [];
        }
    }
}
