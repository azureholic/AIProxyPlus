using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIProxyPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedIdentityConsumers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_consumers_authentication_identity",
                table: "consumers");

            migrationBuilder.AddColumn<Guid>(
                name: "ManagedIdentityObjectId",
                table: "consumers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ManagedIdentityTenantId",
                table: "consumers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consumers_ManagedIdentityTenantId_ManagedIdentityObjectId",
                table: "consumers",
                columns: new[] { "ManagedIdentityTenantId", "ManagedIdentityObjectId" },
                unique: true,
                filter: "\"ManagedIdentityTenantId\" IS NOT NULL AND \"ManagedIdentityObjectId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_consumers_authentication_identity",
                table: "consumers",
                sql: "(\"ApiKeyHash\" IS NOT NULL AND \"OidcIssuer\" IS NULL AND \"OidcSubject\" IS NULL AND \"ManagedIdentityTenantId\" IS NULL AND \"ManagedIdentityObjectId\" IS NULL) OR (\"ApiKeyHash\" IS NULL AND \"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL AND \"ManagedIdentityTenantId\" IS NULL AND \"ManagedIdentityObjectId\" IS NULL) OR (\"ApiKeyHash\" IS NULL AND \"OidcIssuer\" IS NULL AND \"OidcSubject\" IS NULL AND \"ManagedIdentityTenantId\" IS NOT NULL AND \"ManagedIdentityObjectId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_consumers_ManagedIdentityTenantId_ManagedIdentityObjectId",
                table: "consumers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_consumers_authentication_identity",
                table: "consumers");

            migrationBuilder.DropColumn(
                name: "ManagedIdentityObjectId",
                table: "consumers");

            migrationBuilder.DropColumn(
                name: "ManagedIdentityTenantId",
                table: "consumers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_consumers_authentication_identity",
                table: "consumers",
                sql: "(\"ApiKeyHash\" IS NOT NULL AND \"OidcIssuer\" IS NULL AND \"OidcSubject\" IS NULL) OR (\"ApiKeyHash\" IS NULL AND \"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL)");
        }
    }
}
