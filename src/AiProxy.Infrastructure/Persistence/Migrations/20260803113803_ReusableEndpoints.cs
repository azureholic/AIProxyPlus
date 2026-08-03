using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReusableEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_endpoints_model_deployments_ModelDeploymentId",
                table: "endpoints");

            migrationBuilder.DropIndex(
                name: "IX_endpoints_ModelDeploymentId_Priority",
                table: "endpoints");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "endpoints",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "model_deployment_endpoints",
                columns: table => new
                {
                    ModelDeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Health = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_deployment_endpoints", x => new { x.ModelDeploymentId, x.EndpointId });
                    table.ForeignKey(
                        name: "FK_model_deployment_endpoints_endpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "endpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_deployment_endpoints_model_deployments_ModelDeploymen~",
                        column: x => x.ModelDeploymentId,
                        principalTable: "model_deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                UPDATE endpoints
                SET "Name" = 'Endpoint ' || "Id"::text;

                INSERT INTO model_deployment_endpoints
                    ("ModelDeploymentId", "EndpointId", "Priority", "Enabled", "Health")
                SELECT "ModelDeploymentId", "Id", "Priority", "Enabled", "Health"
                FROM endpoints;
                """);

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "Health",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "ModelDeploymentId",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "endpoints");

            migrationBuilder.CreateIndex(
                name: "IX_endpoints_Name",
                table: "endpoints",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_deployment_endpoints_EndpointId",
                table: "model_deployment_endpoints",
                column: "EndpointId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM endpoints e
                        LEFT JOIN model_deployment_endpoints mde ON mde."EndpointId" = e."Id"
                        GROUP BY e."Id"
                        HAVING COUNT(mde."EndpointId") <> 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade while an endpoint is unassigned or assigned to multiple model deployments.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "endpoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Health",
                table: "endpoints",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ModelDeploymentId",
                table: "endpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "endpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE endpoints e
                SET "ModelDeploymentId" = mde."ModelDeploymentId",
                    "Priority" = mde."Priority",
                    "Enabled" = mde."Enabled",
                    "Health" = mde."Health"
                FROM model_deployment_endpoints mde
                WHERE mde."EndpointId" = e."Id";
                """);

            migrationBuilder.DropTable(
                name: "model_deployment_endpoints");

            migrationBuilder.DropIndex(
                name: "IX_endpoints_Name",
                table: "endpoints");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "endpoints");

            migrationBuilder.CreateIndex(
                name: "IX_endpoints_ModelDeploymentId_Priority",
                table: "endpoints",
                columns: new[] { "ModelDeploymentId", "Priority" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_endpoints_model_deployments_ModelDeploymentId",
                table: "endpoints",
                column: "ModelDeploymentId",
                principalTable: "model_deployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
