using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVProssessor.Domain.Migrations
{
    /// <inheritdoc />
    public partial class configfileName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "CsvJobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "CsvJobs");
        }
    }
}
