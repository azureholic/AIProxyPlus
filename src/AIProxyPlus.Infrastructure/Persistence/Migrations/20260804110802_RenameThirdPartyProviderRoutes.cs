using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIProxyPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameThirdPartyProviderRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE api_routes SET "Name" = 'Third Party OpenAI'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01' AND "Name" = 'Foundry OpenAI';

                UPDATE api_routes SET "Name" = 'Third Party Anthropic'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03' AND "Name" = 'Foundry Anthropic';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE api_routes SET "Name" = 'Foundry OpenAI'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01' AND "Name" = 'Third Party OpenAI';

                UPDATE api_routes SET "Name" = 'Foundry Anthropic'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03' AND "Name" = 'Third Party Anthropic';
                """);
        }
    }
}
