using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxChangeFeedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
