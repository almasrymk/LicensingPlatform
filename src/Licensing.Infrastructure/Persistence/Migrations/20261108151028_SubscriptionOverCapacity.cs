using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionOverCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverCapacitySince",
                schema: "subscriptions",
                table: "Subscriptions",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverCapacitySince",
                schema: "subscriptions",
                table: "Subscriptions");
        }
    }
}
