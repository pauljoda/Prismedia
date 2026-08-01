using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEntityProgressUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "progress_updated_at",
                table: "user_entity_states",
                type: "timestamp with time zone",
                nullable: true);

            // Generic updated_at historically served every user-state column family. Only copy it
            // to rows that actually carry a progress signal, so a rating or playback-only update
            // never becomes synthetic reading engagement.
            migrationBuilder.Sql(
                """
                UPDATE user_entity_states
                SET progress_updated_at = updated_at
                WHERE progress_current_entity_id IS NOT NULL
                   OR progress_index <> 0
                   OR progress_total <> 0
                   OR progress_location IS NOT NULL
                   OR progress_completed_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "progress_updated_at",
                table: "user_entity_states");
        }
    }
}
