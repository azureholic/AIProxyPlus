using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOidcConsumerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_consumers_ApiKeyHash",
                table: "consumers");

            migrationBuilder.AlterColumn<string>(
                name: "ApiKeyHash",
                table: "consumers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "OidcIssuer",
                table: "consumers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OidcSubject",
                table: "consumers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consumers_ApiKeyHash",
                table: "consumers",
                column: "ApiKeyHash",
                unique: true,
                filter: "\"ApiKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_consumers_OidcIssuer_OidcSubject",
                table: "consumers",
                columns: new[] { "OidcIssuer", "OidcSubject" },
                unique: true,
                filter: "\"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_consumers_authentication_identity",
                table: "consumers",
                sql: "(\"ApiKeyHash\" IS NOT NULL AND \"OidcIssuer\" IS NULL AND \"OidcSubject\" IS NULL) OR (\"ApiKeyHash\" IS NULL AND \"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_consumers_ApiKeyHash",
                table: "consumers");

            migrationBuilder.DropIndex(
                name: "IX_consumers_OidcIssuer_OidcSubject",
                table: "consumers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_consumers_authentication_identity",
                table: "consumers");

            migrationBuilder.DropColumn(
                name: "OidcIssuer",
                table: "consumers");

            migrationBuilder.DropColumn(
                name: "OidcSubject",
                table: "consumers");

            migrationBuilder.AlterColumn<string>(
                name: "ApiKeyHash",
                table: "consumers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consumers_ApiKeyHash",
                table: "consumers",
                column: "ApiKeyHash",
                unique: true);
        }
    }
}
