namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of statuses for outbound fingerprint submissions.
/// </summary>
public enum FingerprintSubmissionStatus {
    /// <summary>Submission completed successfully.</summary>
    [Code("success")]
    Success,

    /// <summary>Submission failed and the error field should explain why.</summary>
    [Code("error")]
    Error
}
