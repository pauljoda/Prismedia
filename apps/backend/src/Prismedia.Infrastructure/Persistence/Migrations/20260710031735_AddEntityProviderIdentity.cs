using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityProviderIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_provider_identities",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plugin_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    identity_namespace = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    identity_value = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_provider_identities", x => x.entity_id);
                    table.CheckConstraint("ck_entity_provider_identities_namespace_canonical", "identity_namespace = lower(btrim(identity_namespace)) AND identity_namespace <> ''");
                    table.CheckConstraint("ck_entity_provider_identities_plugin_canonical", "plugin_id = lower(btrim(plugin_id)) AND plugin_id <> ''");
                    table.CheckConstraint("ck_entity_provider_identities_value_canonical", "identity_value = btrim(identity_value) AND identity_value <> ''");
                    table.ForeignKey(
                        name: "FK_entity_provider_identities_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_provider_identities");
        }
    }
}
