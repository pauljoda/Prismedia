using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Maps the job_runs aggregate to the PostgreSQL <c>xmin</c> system column as an optimistic
    /// concurrency token. <c>xmin</c> physically exists on every PostgreSQL row, so there is no DDL
    /// to apply or revert; this migration only carries the model-snapshot change forward. EF's diff
    /// emits an AddColumn for the system column, which is intentionally replaced with a no-op here
    /// because creating the column would fail (it already exists).
    /// </summary>
    /// <inheritdoc />
    public partial class AddJobRunConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
