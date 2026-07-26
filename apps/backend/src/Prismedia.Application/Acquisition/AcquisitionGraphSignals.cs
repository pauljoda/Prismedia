namespace Prismedia.Application.Acquisition;

/// <summary>Stable graph-local signal keys used to suspend acquisition workflows without occupying workers.</summary>
public static class AcquisitionGraphSignals {
    public static string Review(Guid acquisitionId) => $"acquisition-review:{acquisitionId}";

    public static string ExternalTransfer(Guid acquisitionId) => $"acquisition-transfer:{acquisitionId}";
}
