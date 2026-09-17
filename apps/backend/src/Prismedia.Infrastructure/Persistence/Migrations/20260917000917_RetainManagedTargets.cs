using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainManagedTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "targets",
                table: "managed_holdings",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
            // Preserve exact established identities. Pending adoptions have no source bindings yet
            // and populate targets when their existing local associations are first verified.
            migrationBuilder.Sql("""
                UPDATE managed_holdings h SET targets = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object(
                        'target', jsonb_build_object('remoteTargetId', b.remote_target_id, 'kind', b.kind,
                            'seasonNumber', b.season_number, 'episodeNumber', b.episode_number, 'absoluteNumber', b.absolute_number),
                        'entityId', b.entity_id) ORDER BY b.remote_target_id)
                    FROM managed_source_bindings b WHERE b.holding_id = h.id
                ), '[]'::jsonb);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "targets",
                table: "managed_holdings");
        }
    }
}
