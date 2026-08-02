using Microsoft.EntityFrameworkCore.Migrations;
using Prismedia.Domain.Entities;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileConsumptionAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var accessedKind = ConsumptionEventKind.Accessed.ToCode();
            migrationBuilder.Sql(
                $"""
                UPDATE user_entity_states AS state
                SET access_count = (
                        SELECT COUNT(*)::integer
                        FROM entity_consumption_events AS event
                        WHERE event.user_id = state.user_id
                          AND event.entity_id = state.entity_id
                          AND event.kind = '{accessedKind}'),
                    last_accessed_at = (
                        SELECT MAX(event.occurred_at)
                        FROM entity_consumption_events AS event
                        WHERE event.user_id = state.user_id
                          AND event.entity_id = state.entity_id
                          AND event.kind = '{accessedKind}');
                """);

            migrationBuilder.DropIndex(
                name: "IX_entity_consumption_events_user_id_session_id_kind",
                table: "entity_consumption_events");

            migrationBuilder.CreateIndex(
                name: "IX_entity_consumption_events_user_id_entity_id_session_id_kind",
                table: "entity_consumption_events",
                columns: new[] { "user_id", "entity_id", "session_id", "kind" },
                unique: true,
                filter: "session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entity_consumption_events_user_id_entity_id_session_id_kind",
                table: "entity_consumption_events");

            migrationBuilder.CreateIndex(
                name: "IX_entity_consumption_events_user_id_session_id_kind",
                table: "entity_consumption_events",
                columns: new[] { "user_id", "session_id", "kind" },
                unique: true,
                filter: "session_id IS NOT NULL");
        }
    }
}
