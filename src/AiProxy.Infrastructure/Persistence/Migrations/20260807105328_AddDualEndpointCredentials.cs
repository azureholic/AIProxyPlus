using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDualEndpointCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SecretRef",
                table: "endpoints",
                newName: "PrimarySecretRef");

            migrationBuilder.AddColumn<string>(
                name: "ActiveCredentialSlot",
                table: "endpoints",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackCredentialSlot",
                table: "endpoints",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FallbackExpiresAt",
                table: "endpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondarySecretRef",
                table: "endpoints",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "endpoints",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.Sql(
                "UPDATE endpoints SET \"ActiveCredentialSlot\" = 'Primary' WHERE \"AuthMode\" = 'ApiKey'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_endpoints_api_key_active_slot",
                table: "endpoints",
                sql: "(\"AuthMode\" = 'ApiKey' AND \"PrimarySecretRef\" IS NOT NULL AND \"ActiveCredentialSlot\" IS NOT NULL AND ((\"ActiveCredentialSlot\" = 'Primary') OR (\"ActiveCredentialSlot\" = 'Secondary' AND \"SecondarySecretRef\" IS NOT NULL))) OR (\"AuthMode\" <> 'ApiKey' AND \"PrimarySecretRef\" IS NULL AND \"SecondarySecretRef\" IS NULL AND \"ActiveCredentialSlot\" IS NULL AND \"FallbackCredentialSlot\" IS NULL AND \"FallbackExpiresAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_endpoints_fallback_slot",
                table: "endpoints",
                sql: "(\"FallbackCredentialSlot\" IS NULL AND \"FallbackExpiresAt\" IS NULL) OR (\"FallbackCredentialSlot\" IS NOT NULL AND \"FallbackExpiresAt\" IS NOT NULL AND \"FallbackCredentialSlot\" <> \"ActiveCredentialSlot\" AND ((\"FallbackCredentialSlot\" = 'Primary' AND \"PrimarySecretRef\" IS NOT NULL) OR (\"FallbackCredentialSlot\" = 'Secondary' AND \"SecondarySecretRef\" IS NOT NULL)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_endpoints_api_key_active_slot",
                table: "endpoints");

            migrationBuilder.DropCheckConstraint(
                name: "CK_endpoints_fallback_slot",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "ActiveCredentialSlot",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "FallbackCredentialSlot",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "FallbackExpiresAt",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "SecondarySecretRef",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "endpoints");

            migrationBuilder.RenameColumn(
                name: "PrimarySecretRef",
                table: "endpoints",
                newName: "SecretRef");
        }
    }
}
