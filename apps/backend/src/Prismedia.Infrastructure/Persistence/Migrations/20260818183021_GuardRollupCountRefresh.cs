using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Infrastructure.Persistence;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuardRollupCountRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replaces the reference/collection count refresh functions with guarded upserts so
            // refreshes stop rewriting identical rows and the reconcile repaired-row count only
            // reports real drift. See EntityRollupProjectionSql.GuardedCountRefresh.
            migrationBuilder.Sql(EntityRollupProjectionSql.GuardedCountRefresh);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The original function bodies are semantically equivalent (they converge to the same
            // rows); rolling back keeps the guarded bodies, which remain compatible with the
            // original migration's tables and triggers.
        }
    }
}
