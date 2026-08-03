using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialModelSchema : Migration
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
                    ApiKeyHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OidcIssuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OidcSubject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumers", x => x.Id);
                    table.CheckConstraint("CK_consumers_authentication_identity", "(\"ApiKeyHash\" IS NOT NULL AND \"OidcIssuer\" IS NULL AND \"OidcSubject\" IS NULL) OR (\"ApiKeyHash\" IS NULL AND \"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ApiFamily = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AuthMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ManagedIdentityScope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ManagedIdentityTenantId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_models", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "session_affinity",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                name: "realtime_tickets",
                columns: table => new
                {
                    TicketHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_realtime_tickets", x => x.TicketHash);
                    table.ForeignKey(
                        name: "FK_realtime_tickets_consumers_ConsumerId",
                        column: x => x.ConsumerId,
                        principalTable: "consumers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "model_endpoints",
                columns: table => new
                {
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Health = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_endpoints", x => new { x.ModelId, x.EndpointId });
                    table.ForeignKey(
                        name: "FK_model_endpoints_endpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "endpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_model_endpoints_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumers_ApiKeyHash",
                table: "consumers",
                column: "ApiKeyHash",
                unique: true,
                filter: "\"ApiKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_consumers_Name",
                table: "consumers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consumers_OidcIssuer_OidcSubject",
                table: "consumers",
                columns: new[] { "OidcIssuer", "OidcSubject" },
                unique: true,
                filter: "\"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_endpoints_Name",
                table: "endpoints",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_model_endpoints_EndpointId",
                table: "model_endpoints",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_models_Name",
                table: "models",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_realtime_tickets_ConsumerId",
                table: "realtime_tickets",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_realtime_tickets_ExpiresAt",
                table: "realtime_tickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_session_affinity_ExpiresAt",
                table: "session_affinity",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_ConsumerId_Timestamp",
                table: "usage_records",
                columns: new[] { "ConsumerId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_ModelName_Timestamp",
                table: "usage_records",
                columns: new[] { "ModelName", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_endpoints");

            migrationBuilder.DropTable(
                name: "realtime_tickets");

            migrationBuilder.DropTable(
                name: "session_affinity");

            migrationBuilder.DropTable(
                name: "usage_records");

            migrationBuilder.DropTable(
                name: "endpoints");

            migrationBuilder.DropTable(
                name: "models");

            migrationBuilder.DropTable(
                name: "consumers");
        }
    }
}
