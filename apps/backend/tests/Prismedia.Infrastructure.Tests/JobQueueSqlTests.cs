using Prismedia.Infrastructure.Queue;

namespace Prismedia.Infrastructure.Tests;

public sealed class JobQueueSqlTests {
    [Fact]
    public void ClaimNextUsesSkipLockedToCoordinateWorkers() {
        var sql = JobQueueSql.ClaimNext;

        Assert.Contains("FOR UPDATE SKIP LOCKED", sql);
        Assert.Contains("status = 'queued'", sql);
        Assert.Contains("available_at <= now()", sql);
        Assert.Contains("ORDER BY priority DESC, available_at, created_at", sql);
        Assert.DoesNotMatch(@"\b(FROM|UPDATE)\.", sql);
    }

    [Fact]
    public void MarkFailedRequeuesUntilMaxAttemptsThenFails() {
        var sql = JobQueueSql.MarkFailed;

        Assert.Contains("attempts < max_attempts", sql);
        Assert.Contains("status = CASE", sql);
        Assert.Contains("'queued'", sql);
        Assert.Contains("'failed'", sql);
        Assert.DoesNotMatch(@"\bUPDATE\.", sql);
    }
}
