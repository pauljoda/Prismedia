using Npgsql;

namespace Prismedia.Infrastructure.Persistence;

/// <summary>Identifies the acquisition ownership database invariant without matching English messages.</summary>
public static class FulfillmentOwnershipViolation {
    /// <summary>Stable PostgreSQL constraint identity used by all ownership guards.</summary>
    public const string ConstraintName = "fulfillment_scope_owner";

    /// <summary>Recognizes both direct bulk-update failures and failures wrapped by EF Core.</summary>
    public static bool IsConflict(Exception error) {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: ConstraintName }) return true;
        return false;
    }
}
