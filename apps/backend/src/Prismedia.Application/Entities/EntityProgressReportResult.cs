namespace Prismedia.Application.Entities;

/// <summary>Outcome category of one progress report.</summary>
public enum EntityProgressReportStatus {
    /// <summary>The report was applied (possibly as a no-op older signal).</summary>
    Applied,

    /// <summary>The work, cursor, or track is missing, hidden, or unrelated.</summary>
    NotFound,

    /// <summary>The report is malformed for the work's declared modalities.</summary>
    Invalid
}

/// <summary>Result of one progress report.</summary>
/// <param name="Status">Outcome category.</param>
/// <param name="OwnerId">Work whose progress was updated, when applied.</param>
/// <param name="Error">Specific validation message, when invalid.</param>
public sealed record EntityProgressReportResult(
    EntityProgressReportStatus Status,
    Guid? OwnerId = null,
    string? Error = null) {
    #region Static Variables

    /// <summary>The work, cursor, or track is missing, hidden, or unrelated.</summary>
    public static EntityProgressReportResult NotFound { get; } = new(EntityProgressReportStatus.NotFound);

    #endregion

    #region Actions - Construction

    /// <summary>The report was applied to <paramref name="ownerId"/>.</summary>
    public static EntityProgressReportResult Applied(Guid ownerId) => new(EntityProgressReportStatus.Applied, ownerId);

    /// <summary>The report was rejected with a specific message.</summary>
    public static EntityProgressReportResult Invalid(string error) => new(EntityProgressReportStatus.Invalid, Error: error);

    #endregion
}
