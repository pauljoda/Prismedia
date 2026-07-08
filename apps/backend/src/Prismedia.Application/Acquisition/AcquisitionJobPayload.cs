using System.Text.Json;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Payload for acquisition background jobs (search, monitor, import): the acquisition id they act on.
/// <see cref="AllowFormatChange"/> rides only on a manual retry-import — the user's explicit consent for
/// an upgrade that swaps the owned file's format; absent (false) on every automatic enqueue.
/// </summary>
public sealed record AcquisitionJobPayload(Guid AcquisitionId, bool AllowFormatChange = false) {
    public static string Serialize(Guid acquisitionId, bool allowFormatChange = false) =>
        JsonSerializer.Serialize(new AcquisitionJobPayload(acquisitionId, allowFormatChange));

    public static AcquisitionJobPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<AcquisitionJobPayload>(payloadJson)
            ?? throw new InvalidOperationException("Acquisition job payload is missing or invalid.");
}
