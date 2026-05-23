using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Queue;

public static class JobQueueSql {
    public static readonly string ClaimNext = $$"""
        WITH next_job AS (
            SELECT id
            FROM job_runs
            WHERE status = '{{JobRunStatus.Queued.ToCode()}}'
              AND available_at <= now()
            ORDER BY priority DESC, available_at, created_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1
        )
        UPDATE job_runs job
        SET
            status = '{{JobRunStatus.Running.ToCode()}}',
            locked_at = now(),
            locked_by = @worker_id,
            started_at = COALESCE(started_at, now()),
            attempts = attempts + 1
        FROM next_job
        WHERE job.id = next_job.id
        RETURNING job.*;
        """;

    public static readonly string MarkCompleted = $$"""
        UPDATE job_runs
        SET
            status = '{{JobRunStatus.Completed.ToCode()}}',
            progress = 100,
            message = @message,
            locked_at = NULL,
            locked_by = NULL,
            finished_at = now()
        WHERE id = @id;
        """;

    public static readonly string MarkFailed = $$"""
        UPDATE job_runs
        SET
            status = CASE
                WHEN attempts < max_attempts THEN '{{JobRunStatus.Queued.ToCode()}}'
                ELSE '{{JobRunStatus.Failed.ToCode()}}'
            END,
            message = @message,
            locked_at = NULL,
            locked_by = NULL,
            available_at = CASE
                WHEN attempts < max_attempts THEN now() + (@retry_delay_seconds || ' seconds')::interval
                ELSE available_at
            END,
            finished_at = CASE
                WHEN attempts < max_attempts THEN NULL
                ELSE now()
            END
        WHERE id = @id;
        """;
}
