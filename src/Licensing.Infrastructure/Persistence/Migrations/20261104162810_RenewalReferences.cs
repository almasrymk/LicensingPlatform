using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reference",
                schema: "subscriptions",
                table: "SubscriptionHistory",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionHistory_SubscriptionId_Reference",
                schema: "subscriptions",
                table: "SubscriptionHistory",
                columns: new[] { "SubscriptionId", "Reference" },
                unique: true,
                filter: "[Reference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionHistory_SubscriptionId_Reference",
                schema: "subscriptions",
                table: "SubscriptionHistory");

            migrationBuilder.DropColumn(
                name: "Reference",
                schema: "subscriptions",
                table: "SubscriptionHistory");
        }
    }
}
