using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReusableApiRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_model_endpoints",
                table: "model_endpoints");

            migrationBuilder.RenameColumn(
                name: "ApiFamily",
                table: "usage_records",
                newName: "Provider");

            migrationBuilder.CreateTable(
                name: "api_routes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PathPrefix = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_routes", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO api_routes ("Id", "Name", "Provider", "PathPrefix", "IsBuiltIn") VALUES
                    ('7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01', 'Foundry OpenAI', 'OpenAI', '/openai/v1', TRUE),
                    ('7d746f5a-9a9b-4cf4-91e8-5fa231ae9c02', 'Microsoft AI', 'MicrosoftAI', '/mai/v1', TRUE),
                    ('7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03', 'Anthropic', 'Anthropic', '/anthropic/v1', TRUE),
                    ('7d746f5a-9a9b-4cf4-91e8-5fa231ae9c04', 'Native OpenAI', 'OpenAI', '/v1', TRUE);

                UPDATE usage_records
                SET "Provider" = CASE "Provider"
                    WHEN 'AzureOpenAI' THEN 'OpenAI'
                    WHEN 'OpenAICompatible' THEN 'OpenAI'
                    ELSE "Provider"
                END;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ModelEndpointId",
                table: "session_affinity",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "model_endpoints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiRouteId",
                table: "model_endpoints",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE model_endpoints AS assignment
                SET "Id" = md5(assignment."ModelId"::text || ':' || assignment."EndpointId"::text)::uuid,
                    "ApiRouteId" = CASE endpoint."ApiFamily"
                        WHEN 'OpenAI' THEN '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c04'::uuid
                        WHEN 'Anthropic' THEN '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03'::uuid
                        WHEN 'AzureOpenAI' THEN '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01'::uuid
                        WHEN 'OpenAICompatible' THEN '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01'::uuid
                    END
                FROM endpoints AS endpoint
                WHERE endpoint."Id" = assignment."EndpointId";

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM model_endpoints WHERE "Id" IS NULL OR "ApiRouteId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot map one or more legacy endpoint API families to reusable API routes.';
                    END IF;
                END $$;

                UPDATE session_affinity AS affinity
                SET "ModelEndpointId" = assignment."Id"
                FROM model_endpoints AS assignment
                WHERE assignment."ModelId" = affinity."ModelId"
                  AND assignment."EndpointId" = affinity."EndpointId";

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM session_affinity WHERE "ModelEndpointId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot map one or more affinity records to a model endpoint assignment.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ModelEndpointId",
                table: "session_affinity",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "model_endpoints",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ApiRouteId",
                table: "model_endpoints",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ApiFamily",
                table: "endpoints");

            migrationBuilder.AddPrimaryKey(
                name: "PK_model_endpoints",
                table: "model_endpoints",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_model_endpoints_ApiRouteId",
                table: "model_endpoints",
                column: "ApiRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_model_endpoints_ModelId_EndpointId_ApiRouteId",
                table: "model_endpoints",
                columns: new[] { "ModelId", "EndpointId", "ApiRouteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_routes_Name",
                table: "api_routes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_routes_Provider_PathPrefix",
                table: "api_routes",
                columns: new[] { "Provider", "PathPrefix" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_model_endpoints_api_routes_ApiRouteId",
                table: "model_endpoints",
                column: "ApiRouteId",
                principalTable: "api_routes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_model_endpoints_api_routes_ApiRouteId",
                table: "model_endpoints");

            migrationBuilder.DropPrimaryKey(
                name: "PK_model_endpoints",
                table: "model_endpoints");

            migrationBuilder.DropIndex(
                name: "IX_model_endpoints_ApiRouteId",
                table: "model_endpoints");

            migrationBuilder.DropIndex(
                name: "IX_model_endpoints_ModelId_EndpointId_ApiRouteId",
                table: "model_endpoints");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM model_endpoints
                        GROUP BY "ModelId", "EndpointId"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot downgrade while a model uses one endpoint through multiple API routes.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "ApiFamily",
                table: "endpoints",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE endpoints AS endpoint
                SET "ApiFamily" = CASE
                    WHEN route."Provider" = 'Anthropic' THEN 'Anthropic'
                    WHEN route."Provider" = 'OpenAI' AND route."PathPrefix" = '/v1' THEN 'OpenAI'
                    WHEN route."Provider" = 'OpenAI' AND route."PathPrefix" = '/openai/v1' THEN 'AzureOpenAI'
                    WHEN route."Provider" = 'MicrosoftAI' THEN 'AzureOpenAI'
                    ELSE 'OpenAICompatible'
                END
                FROM model_endpoints AS assignment
                JOIN api_routes AS route ON route."Id" = assignment."ApiRouteId"
                WHERE assignment."EndpointId" = endpoint."Id";

                UPDATE endpoints
                SET "ApiFamily" = 'OpenAICompatible'
                WHERE "ApiFamily" IS NULL;

                UPDATE usage_records
                SET "Provider" = 'AzureOpenAI'
                WHERE "Provider" = 'MicrosoftAI';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ApiFamily",
                table: "endpoints",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ModelEndpointId",
                table: "session_affinity");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "model_endpoints");

            migrationBuilder.DropColumn(
                name: "ApiRouteId",
                table: "model_endpoints");

            migrationBuilder.DropTable(
                name: "api_routes");

            migrationBuilder.RenameColumn(
                name: "Provider",
                table: "usage_records",
                newName: "ApiFamily");

            migrationBuilder.AddPrimaryKey(
                name: "PK_model_endpoints",
                table: "model_endpoints",
                columns: new[] { "ModelId", "EndpointId" });
        }
    }
}
