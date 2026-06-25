using System.Text.Json;

namespace Prismedia.Application.Acquisition;

/// <summary>Payload for acquisition background jobs (search, monitor, import): the acquisition id they act on.</summary>
public sealed record AcquisitionJobPayload(Guid AcquisitionId) {
    public static string Serialize(Guid acquisitionId) => JsonSerializer.Serialize(new AcquisitionJobPayload(acquisitionId));

    public static AcquisitionJobPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<AcquisitionJobPayload>(payloadJson)
            ?? throw new InvalidOperationException("Acquisition job payload is missing or invalid.");
}
