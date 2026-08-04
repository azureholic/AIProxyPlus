using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DualConsumerKeysAndModelAllowLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHash2",
                table: "consumers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "consumer_allowed_models",
                columns: table => new
                {
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_allowed_models", x => new { x.ConsumerId, x.ModelId });
                    table.ForeignKey(
                        name: "FK_consumer_allowed_models_consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_consumer_allowed_models_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumers_ApiKeyHash2",
                table: "consumers",
                column: "ApiKeyHash2",
                unique: true,
                filter: "\"ApiKeyHash2\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_consumers_secondary_key_requires_primary",
                table: "consumers",
                sql: "\"ApiKeyHash2\" IS NULL OR \"ApiKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_allowed_models_ModelId",
                table: "consumer_allowed_models",
                column: "ModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_allowed_models");

            migrationBuilder.DropIndex(
                name: "IX_consumers_ApiKeyHash2",
                table: "consumers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_consumers_secondary_key_requires_primary",
                table: "consumers");

            migrationBuilder.DropColumn(
                name: "ApiKeyHash2",
                table: "consumers");
        }
    }
}
