using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_capabilities",
                columns: table => new
                {
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_capabilities", x => new { x.ModelId, x.Capability });
                    table.ForeignKey(
                        name: "FK_model_capabilities_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

                    migrationBuilder.Sql(
                    """
                    INSERT INTO model_capabilities ("ModelId", "Capability")
                    SELECT models."Id", capabilities."Capability"
                    FROM models
                    CROSS JOIN (VALUES ('Chat'), ('ImageGeneration'), ('Realtime')) AS capabilities("Capability");
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_capabilities");
        }
    }
}
