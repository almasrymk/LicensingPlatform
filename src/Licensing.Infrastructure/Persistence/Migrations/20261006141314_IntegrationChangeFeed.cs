using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationChangeFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                schema: "messaging",
                table: "OutboxMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId_OccurredAt_Id",
                schema: "messaging",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_TenantId_OccurredAt_Id",
                schema: "messaging",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                schema: "messaging",
                table: "OutboxMessages");
        }
    }
}
