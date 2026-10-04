using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductPlatformsAndDeviceOs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Platforms",
                schema: "catalog",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                schema: "licensing",
                table: "LicenseActivations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Platforms",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                schema: "licensing",
                table: "LicenseActivations");
        }
    }
}
