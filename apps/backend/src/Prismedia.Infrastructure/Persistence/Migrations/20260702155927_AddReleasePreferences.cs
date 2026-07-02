using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleasePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "preferred_languages",
                table: "book_acquisition_profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            // Carry the old single required-language forward as the profile's top preference; profiles
            // that never set one get the product default of English.
            migrationBuilder.Sql(
                """
                UPDATE book_acquisition_profiles
                SET preferred_languages = CASE
                    WHEN language IS NOT NULL AND btrim(language) <> '' THEN ARRAY[btrim(language)]
                    ELSE ARRAY['English']
                END;
                """);

            migrationBuilder.DropColumn(
                name: "language",
                table: "book_acquisition_profiles");

            migrationBuilder.AddColumn<string>(
                name: "weighted_terms_json",
                table: "book_acquisition_profiles",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preferred_languages",
                table: "book_acquisition_profiles");

            migrationBuilder.DropColumn(
                name: "weighted_terms_json",
                table: "book_acquisition_profiles");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "book_acquisition_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
