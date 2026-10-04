using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaImagesAndProductIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.AddColumn<Guid>(
                name: "ImageId",
                schema: "identity",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImageId",
                schema: "tenants",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                schema: "catalog",
                table: "Products",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImageId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImageId",
                schema: "customers",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", maxLength: -1, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_OwnerType_OwnerId",
                schema: "media",
                table: "MediaFiles",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaFiles",
                schema: "media");

            migrationBuilder.DropColumn(
                name: "ImageId",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ImageId",
                schema: "tenants",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Icon",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImageId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImageId",
                schema: "customers",
                table: "Customers");
        }
    }
}
