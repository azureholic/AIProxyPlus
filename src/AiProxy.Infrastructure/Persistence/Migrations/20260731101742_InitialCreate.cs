using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_deployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StickySessionEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_deployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "session_affinity",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModelDeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_affinity", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModelDeploymentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiFamily = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    TotalTokens = table.Column<int>(type: "integer", nullable: true),
                    UsageMetrics = table.Column<string>(type: "jsonb", nullable: true),
                    Streaming = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelDeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApiFamily = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AuthMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ManagedIdentityScope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false),
                    Health = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_endpoints_model_deployments_ModelDeploymentId",
                        column: x => x.ModelDeploymentId,
                        principalTable: "model_deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumers_ApiKeyHash",
                table: "consumers",
                column: "ApiKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consumers_Name",
                table: "consumers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_endpoints_ModelDeploymentId_Priority",
                table: "endpoints",
                columns: new[] { "ModelDeploymentId", "Priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_deployments_Name",
                table: "model_deployments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_session_affinity_ExpiresAt",
                table: "session_affinity",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_ConsumerId_Timestamp",
                table: "usage_records",
                columns: new[] { "ConsumerId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_ModelDeploymentName_Timestamp",
                table: "usage_records",
                columns: new[] { "ModelDeploymentName", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumers");

            migrationBuilder.DropTable(
                name: "endpoints");

            migrationBuilder.DropTable(
                name: "session_affinity");

            migrationBuilder.DropTable(
                name: "usage_records");

            migrationBuilder.DropTable(
                name: "model_deployments");
        }
    }
}
