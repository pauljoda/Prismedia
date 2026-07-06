using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prismedia.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Multi-user authentication cutover. Hand-edited from the scaffolded drop/create
    /// into rename operations so existing installs keep their data: Jellyfin profiles
    /// become user accounts (same ids), Jellyfin sessions become user sessions (same
    /// token hashes, so signed-in clients stay signed in), and the app API key is staged
    /// into <c>legacy_api_key</c> for the startup bootstrap to hash into migrated
    /// accounts' passwords.
    /// </summary>
    public partial class AddUserAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // app_security: stage the old key for the password bootstrap, drop key bookkeeping.
            migrationBuilder.RenameColumn(
                name: "api_key",
                table: "app_security",
                newName: "legacy_api_key");

            migrationBuilder.AlterColumn<string>(
                name: "legacy_api_key",
                table: "app_security",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.DropColumn(
                name: "api_key_created_at",
                table: "app_security");

            migrationBuilder.DropColumn(
                name: "api_key_updated_at",
                table: "app_security");

            migrationBuilder.DropColumn(
                name: "default_profile_seeded",
                table: "app_security");

            // jellyfin_profiles -> users (rows, ids, and the username index survive).
            migrationBuilder.RenameTable(
                name: "jellyfin_profiles",
                newName: "users");

            migrationBuilder.Sql("""ALTER TABLE users RENAME CONSTRAINT "PK_jellyfin_profiles" TO "PK_users";""");

            migrationBuilder.RenameIndex(
                name: "IX_jellyfin_profiles_normalized_username",
                table: "users",
                newName: "IX_users_normalized_username");

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_updated_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "member");

            migrationBuilder.AddColumn<bool>(
                name: "can_create_libraries",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The defaults above only backfill existing rows. The model writes role and
            // allow_sfw explicitly (a lingering default would mask sentinel-value inserts),
            // so drop the column defaults once the backfill is done.
            migrationBuilder.Sql("""ALTER TABLE users ALTER COLUMN role DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE users ALTER COLUMN allow_sfw DROP DEFAULT;""");

            // jellyfin_sessions -> user_sessions (token hashes survive: clients stay signed in).
            migrationBuilder.RenameTable(
                name: "jellyfin_sessions",
                newName: "user_sessions");

            migrationBuilder.RenameColumn(
                name: "profile_id",
                table: "user_sessions",
                newName: "user_id");

            migrationBuilder.Sql("""ALTER TABLE user_sessions RENAME CONSTRAINT "PK_jellyfin_sessions" TO "PK_user_sessions";""");
            migrationBuilder.Sql("""ALTER TABLE user_sessions RENAME CONSTRAINT "FK_jellyfin_sessions_jellyfin_profiles_profile_id" TO "FK_user_sessions_users_user_id";""");

            migrationBuilder.RenameIndex(
                name: "IX_jellyfin_sessions_token_hash",
                table: "user_sessions",
                newName: "IX_user_sessions_token_hash");

            migrationBuilder.RenameIndex(
                name: "IX_jellyfin_sessions_profile_id_invalidated_at",
                table: "user_sessions",
                newName: "IX_user_sessions_user_id_invalidated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The pre-auth model declared a database default for allow_sfw.
            migrationBuilder.Sql("""ALTER TABLE users ALTER COLUMN allow_sfw SET DEFAULT true;""");

            migrationBuilder.RenameIndex(
                name: "IX_user_sessions_user_id_invalidated_at",
                table: "user_sessions",
                newName: "IX_jellyfin_sessions_profile_id_invalidated_at");

            migrationBuilder.RenameIndex(
                name: "IX_user_sessions_token_hash",
                table: "user_sessions",
                newName: "IX_jellyfin_sessions_token_hash");

            migrationBuilder.Sql("""ALTER TABLE user_sessions RENAME CONSTRAINT "FK_user_sessions_users_user_id" TO "FK_jellyfin_sessions_jellyfin_profiles_profile_id";""");
            migrationBuilder.Sql("""ALTER TABLE user_sessions RENAME CONSTRAINT "PK_user_sessions" TO "PK_jellyfin_sessions";""");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "user_sessions",
                newName: "profile_id");

            migrationBuilder.RenameTable(
                name: "user_sessions",
                newName: "jellyfin_sessions");

            migrationBuilder.DropColumn(
                name: "can_create_libraries",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_updated_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "users");

            migrationBuilder.RenameIndex(
                name: "IX_users_normalized_username",
                table: "users",
                newName: "IX_jellyfin_profiles_normalized_username");

            migrationBuilder.Sql("""ALTER TABLE users RENAME CONSTRAINT "PK_users" TO "PK_jellyfin_profiles";""");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "jellyfin_profiles");

            migrationBuilder.AddColumn<bool>(
                name: "default_profile_seeded",
                table: "app_security",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "api_key_created_at",
                table: "app_security",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "api_key_updated_at",
                table: "app_security",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<string>(
                name: "legacy_api_key",
                table: "app_security",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "legacy_api_key",
                table: "app_security",
                newName: "api_key");
        }
    }
}
