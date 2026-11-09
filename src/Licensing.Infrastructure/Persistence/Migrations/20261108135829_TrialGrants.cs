using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrialGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrialGrants",
                schema: "licensing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RedeemedDeviceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LicenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialGrants_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrialGrants_CodeHash",
                schema: "licensing",
                table: "TrialGrants",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialGrants_ProductId",
                schema: "licensing",
                table: "TrialGrants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialGrants_TenantId_ProductId_Reference",
                schema: "licensing",
                table: "TrialGrants",
                columns: new[] { "TenantId", "ProductId", "Reference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrialGrants",
                schema: "licensing");
        }
    }
}
