using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionActiveDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveDevices",
                schema: "subscriptions",
                table: "Subscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // INT-015: start from the devices the subscription's licenses already hold.
            migrationBuilder.Sql("""
                UPDATE s SET ActiveDevices = ISNULL((SELECT SUM(l.ActiveActivations) FROM licensing.Licenses l WHERE l.SubscriptionId = s.Id), 0)
                FROM subscriptions.Subscriptions s;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveDevices",
                schema: "subscriptions",
                table: "Subscriptions");
        }
    }
}
