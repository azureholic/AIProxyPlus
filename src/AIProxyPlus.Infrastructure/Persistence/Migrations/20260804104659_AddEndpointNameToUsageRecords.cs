using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIProxyPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointNameToUsageRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndpointName",
                table: "usage_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE usage_records AS usage
                SET "EndpointName" = endpoint."Name"
                FROM endpoints AS endpoint
                WHERE endpoint."Id" = usage."EndpointId";

                UPDATE usage_records
                SET "EndpointName" = 'unknown'
                WHERE "EndpointName" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "EndpointName",
                table: "usage_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndpointName",
                table: "usage_records");
        }
    }
}
