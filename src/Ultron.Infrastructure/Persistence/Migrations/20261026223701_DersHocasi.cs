using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ultron.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DersHocasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Instructor",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Instructor",
                table: "CalendarEvents");
        }
    }
}
