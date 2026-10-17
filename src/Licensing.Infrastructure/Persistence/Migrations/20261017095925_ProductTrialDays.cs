using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductTrialDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                schema: "catalog",
                table: "Products",
                type: "int",
                nullable: true);

            // Each product keeps the trial length devices got before: its current trial plan's, else the first published
            // non-free plan with trial days (the old fallback). Trial plans (Kind 2) then become archived paid plans, so
            // subscriptions already on them keep working.
            // EXEC defers compiling the UPDATE: in an idempotent script it shares a batch with the ADD COLUMN above,
            // and SQL Server would otherwise reject Products.TrialDays as an unknown column before anything runs.
            migrationBuilder.Sql("""
                EXEC(N'
                UPDATE p SET TrialDays = t.TrialDays
                FROM catalog.Products p
                CROSS APPLY (
                    SELECT TOP 1 pl.TrialDays FROM catalog.Plans pl
                    WHERE pl.ProductId = p.Id AND pl.Status = 2 AND pl.TrialDays > 0 AND pl.Kind <> 3
                    ORDER BY CASE WHEN pl.Kind = 2 AND pl.IsCurrent = 1 THEN 0 ELSE 1 END, pl.Code, pl.Version DESC) t;

                UPDATE catalog.Plans SET Kind = 1, Status = 3, IsCurrent = 0 WHERE Kind = 2;');
                """);

            migrationBuilder.DropColumn(
                name: "TrialDays",
                schema: "catalog",
                table: "Plans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialDays",
                schema: "catalog",
                table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                schema: "catalog",
                table: "Plans",
                type: "int",
                nullable: true);
        }
    }
}
