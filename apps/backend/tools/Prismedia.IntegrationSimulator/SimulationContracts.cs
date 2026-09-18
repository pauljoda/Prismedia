namespace Prismedia.IntegrationSimulator;

/// <summary>Explicit simulator controls; these are not part of the proposed production Archiver API.</summary>
public sealed record SimulationControls(bool LoseNextSubmissionResponse = false, bool LoseNextReceiptResponse = false, int ExecutionDelaySeconds = 1);
/// <summary>Typed internal rejection translated to an HTTP problem.</summary>
public sealed class ApiFailure(int status, string code, string message) : Exception(message) {
    public int Status { get; } = status;
    public string Code { get; } = code;
}
