using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeEntityExternalIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM entity_external_ids
                        GROUP BY entity_id, lower(btrim(provider))
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot normalize entity external identities because an entity has duplicate provider namespaces after trimming and lower-casing.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                UPDATE entity_external_ids
                SET provider = lower(btrim(provider)),
                    value = btrim(value)
                WHERE provider <> lower(btrim(provider))
                   OR value <> btrim(value);
                """);

            migrationBuilder.DropIndex(
                name: "IX_entity_external_ids_provider",
                table: "entity_external_ids");

            migrationBuilder.CreateIndex(
                name: "IX_entity_external_ids_provider_value_entity_id",
                table: "entity_external_ids",
                columns: new[] { "provider", "value", "entity_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_external_ids_provider_canonical",
                table: "entity_external_ids",
                sql: "provider = lower(btrim(provider)) AND provider <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_external_ids_value_canonical",
                table: "entity_external_ids",
                sql: "value = btrim(value) AND value <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entity_external_ids_provider_value_entity_id",
                table: "entity_external_ids");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_external_ids_provider_canonical",
                table: "entity_external_ids");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_external_ids_value_canonical",
                table: "entity_external_ids");

            migrationBuilder.CreateIndex(
                name: "IX_entity_external_ids_provider",
                table: "entity_external_ids",
                column: "provider");
        }
    }
}
