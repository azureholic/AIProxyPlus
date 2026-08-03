using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_realtime_tickets_ConsumerId",
                table: "realtime_tickets",
                column: "ConsumerId");

            migrationBuilder.CreateIndex(
                name: "IX_realtime_tickets_ExpiresAt",
                table: "realtime_tickets",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "realtime_tickets");
        }
    }
}
