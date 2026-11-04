using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuspensionOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SuspendedByCustomer",
                schema: "subscriptions",
                table: "Subscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuspendedByCustomer",
                schema: "licensing",
                table: "Licenses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuspendedByCustomer",
                schema: "subscriptions",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "SuspendedByCustomer",
                schema: "licensing",
                table: "Licenses");
        }
    }
}
