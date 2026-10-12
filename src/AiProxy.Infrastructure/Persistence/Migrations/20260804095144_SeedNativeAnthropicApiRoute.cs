using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedNativeAnthropicApiRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE api_routes
                SET "IsBuiltIn" = TRUE
                WHERE "Provider" = 'Anthropic' AND "PathPrefix" = '/v1';

                INSERT INTO api_routes ("Id", "Name", "Provider", "PathPrefix", "IsBuiltIn")
                SELECT
                    '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c05',
                    'Native Anthropic',
                    'Anthropic',
                    '/v1',
                    TRUE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM api_routes
                    WHERE "Provider" = 'Anthropic' AND "PathPrefix" = '/v1'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM api_routes
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c05';

                UPDATE api_routes
                SET "IsBuiltIn" = FALSE
                WHERE "Provider" = 'Anthropic' AND "PathPrefix" = '/v1';
                """);
        }
    }
}
