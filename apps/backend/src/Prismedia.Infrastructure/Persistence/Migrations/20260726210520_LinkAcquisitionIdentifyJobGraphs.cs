using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkAcquisitionIdentifyJobGraphs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "job_graph_id",
                table: "identify_queue_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "job_graph_id",
                table: "acquisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE identify_queue_items AS item
                SET job_graph_id = COALESCE(search.graph_id, cascade.graph_id)
                FROM identify_queue_items AS source
                LEFT JOIN job_runs AS search ON search.id = source.search_job_id
                LEFT JOIN job_runs AS cascade ON cascade.id = source.cascade_job_id
                WHERE item.id = source.id
                  AND COALESCE(search.graph_id, cascade.graph_id) IS NOT NULL;

                UPDATE acquisitions AS acquisition
                SET job_graph_id = (
                    SELECT run.graph_id
                    FROM job_runs AS run
                    WHERE run.target_entity_id = acquisition.id::text
                      AND run.graph_id IS NOT NULL
                    ORDER BY
                        CASE run.status WHEN 'running' THEN 0 WHEN 'queued' THEN 1 ELSE 2 END,
                        run.created_at DESC
                    LIMIT 1
                )
                WHERE acquisition.job_graph_id IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM job_runs AS run
                    WHERE run.target_entity_id = acquisition.id::text
                      AND run.graph_id IS NOT NULL
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_identify_queue_items_job_graph_id",
                table: "identify_queue_items",
                column: "job_graph_id");

            migrationBuilder.CreateIndex(
                name: "IX_acquisitions_job_graph_id",
                table: "acquisitions",
                column: "job_graph_id");

            migrationBuilder.AddForeignKey(
                name: "FK_acquisitions_job_graphs_job_graph_id",
                table: "acquisitions",
                column: "job_graph_id",
                principalTable: "job_graphs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_identify_queue_items_job_graphs_job_graph_id",
                table: "identify_queue_items",
                column: "job_graph_id",
                principalTable: "job_graphs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_acquisitions_job_graphs_job_graph_id",
                table: "acquisitions");

            migrationBuilder.DropForeignKey(
                name: "FK_identify_queue_items_job_graphs_job_graph_id",
                table: "identify_queue_items");

            migrationBuilder.DropIndex(
                name: "IX_identify_queue_items_job_graph_id",
                table: "identify_queue_items");

            migrationBuilder.DropIndex(
                name: "IX_acquisitions_job_graph_id",
                table: "acquisitions");

            migrationBuilder.DropColumn(
                name: "job_graph_id",
                table: "identify_queue_items");

            migrationBuilder.DropColumn(
                name: "job_graph_id",
                table: "acquisitions");
        }
    }
}
