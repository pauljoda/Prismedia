namespace Prismedia.Application.Jobs;

/// <summary>
/// Aggregate count of job runs sharing a type and status, used by the dashboard
/// to display accurate totals without fetching every row.
/// </summary>
/// <param name="TypeCode">Job type code (e.g. "scan-library").</param>
/// <param name="StatusCode">Job status code (e.g. "queued", "running").</param>
/// <param name="Count">Number of job runs with this type and status.</param>
public sealed record JobQueueCount(string TypeCode, string StatusCode, int Count);
