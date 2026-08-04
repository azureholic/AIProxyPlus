using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiProxy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameFoundryProviderRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE api_routes SET "Name" = 'Foundry Anthropic'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03' AND "Name" = 'Anthropic';

                UPDATE api_routes SET "Name" = 'Foundry Microsoft AI'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c02' AND "Name" = 'Microsoft AI';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE api_routes SET "Name" = 'Anthropic'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03' AND "Name" = 'Foundry Anthropic';

                UPDATE api_routes SET "Name" = 'Microsoft AI'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c02' AND "Name" = 'Foundry Microsoft AI';
                """);
        }
    }
}
