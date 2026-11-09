using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                schema: "subscriptions",
                table: "Subscriptions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId_ExternalReference",
                schema: "subscriptions",
                table: "Subscriptions",
                columns: new[] { "TenantId", "ExternalReference" },
                unique: true,
                filter: "[ExternalReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_ExternalReference",
                schema: "customers",
                table: "Customers",
                columns: new[] { "TenantId", "ExternalReference" },
                unique: true,
                filter: "[ExternalReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_TenantId_ExternalReference",
                schema: "subscriptions",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_ExternalReference",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                schema: "subscriptions",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                schema: "customers",
                table: "Customers");
        }
    }
}
