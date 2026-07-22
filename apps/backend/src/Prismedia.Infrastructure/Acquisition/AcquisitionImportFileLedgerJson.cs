using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Acquisition;

internal static class AcquisitionImportFileLedgerJson {
    private static readonly JsonSerializerOptions Options = new() {
        Converters = { new CodecJsonConverterFactory() }
    };

    public static string Serialize(AcquisitionImportFileLedger ledger) =>
        JsonSerializer.Serialize(ledger, Options);

    public static AcquisitionImportFileLedger? Deserialize(string? json) {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        return JsonSerializer.Deserialize<AcquisitionImportFileLedger>(json, Options);
    }

    public static bool TryDeserialize(string? json, out AcquisitionImportFileLedger? ledger) {
        try {
            ledger = Deserialize(json);
            return true;
        } catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) {
            ledger = null;
            return false;
        }
    }
}
