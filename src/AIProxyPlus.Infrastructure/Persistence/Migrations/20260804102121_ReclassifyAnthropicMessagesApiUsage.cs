using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIProxyPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReclassifyAnthropicMessagesApiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE usage_records SET "OperationType" = 'MessagesApi'
                WHERE "Provider" = 'Anthropic' AND "OperationType" = 'ChatCompletion';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE usage_records SET "OperationType" = 'ChatCompletion'
                WHERE "Provider" = 'Anthropic' AND "OperationType" = 'MessagesApi';
                """);
        }
    }
}
